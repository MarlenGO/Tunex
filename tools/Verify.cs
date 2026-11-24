using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace Tunex
{
    // Проверка, что Windows реально даёт изменить каждую настройку: записать → прочитать → вернуть.
    // Запуск (от администратора): tools\bin\Verify.exe отчёт.txt
    static class Verify
    {
        static StringBuilder log = new StringBuilder();
        static int fails;

        static void Ok(bool ok, string what) { log.AppendLine((ok ? "OK    " : "FAIL  ") + what); if (!ok) fails++; }
        static void Info(string what) { log.AppendLine("      " + what); }

        static void Main(string[] args)
        {
            SysInfo.InitFast();
            Catalog.Build();
            Info("Администратор: " + SysInfo.IsAdmin + ", сборка Windows " + SysInfo.Build);
            Run("Электропитание", Power_);
            Run("Реестр HKLM/HKCU", Registry_);
            Run("Службы", Services_);
            Run("Память", Memory_);
            Run("Сеть", Net_);
            Run("Приоритет процессов", Priority_);
            Run("Файл подкачки", Pagefile_);
            log.AppendLine(fails == 0 ? "ИТОГ: всё работает" : "ИТОГ: проблем " + fails);
            File.WriteAllText(args[0], log.ToString(), Encoding.UTF8);
        }

        static void Run(string title, Action a)
        {
            log.AppendLine("== " + title);
            try { a(); } catch (Exception ex) { Ok(false, title + ": " + ex.Message); }
        }

        static void Power_()
        {
            Guid s = Power.Active();
            string[][] items =
            {
                new string[] { "Парковка ядер (CPMINCORES)", "54533251-82be-4824-96c1-47b60b740d00", "0cc5b647-c1df-4637-891a-dec35c318583", "50" },
                new string[] { "Мин. состояние ЦП", "54533251-82be-4824-96c1-47b60b740d00", "893dee8e-2bef-41e0-89c6-b55d0929964c", "100" },
                new string[] { "PCIe ASPM", "501a4d13-42af-4429-9fd1-a8218c268e20", "ee12f906-d277-404b-b6da-e5fa1a576df5", "0" },
                new string[] { "USB приостановка", "2a737441-1930-4402-8d77-b2bebba308a3", "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", "0" }
            };
            foreach (string[] it in items)
            {
                Guid sub = new Guid(it[1]), set = new Guid(it[2]); uint test = uint.Parse(it[3]);
                uint orig = Power.ReadAC(s, sub, set);
                Power.WriteAC(s, sub, set, test); Power.SetActive(s);
                uint now = Power.ReadAC(s, sub, set);
                Power.WriteAC(s, sub, set, orig); Power.SetActive(s);
                uint back = Power.ReadAC(s, sub, set);
                Ok(now == test && back == orig, it[0] + ": было " + orig + " → записали " + test + " → прочитали " + now + " → вернули " + back);
            }
        }

        static void Registry_()
        {
            foreach (KeyValuePair<string, Tweak> kv in Catalog.All)
            {
                RegistryTweak t = kv.Value as RegistryTweak;
                if (t == null) continue;
                foreach (RegSpec sp in t.Specs)
                {
                    string where = t.Id + " " + sp.Root + "\\…\\" + sp.Name;
                    try
                    {
                        object v = Reg.Get(sp.Root, sp.Path, sp.Name);
                        if (v != null) { Reg.Set(sp.Root, sp.Path, sp.Name, v, Reg.Kind(sp.Root, sp.Path, sp.Name)); Ok(true, where + ": запись разрешена"); }
                        else
                        {
                            Reg.Set(sp.Root, sp.Path, sp.Name, sp.On, sp.Kind);
                            bool wrote = Reg.Get(sp.Root, sp.Path, sp.Name) != null;
                            Reg.Delete(sp.Root, sp.Path, sp.Name);
                            Ok(wrote && Reg.Get(sp.Root, sp.Path, sp.Name) == null, where + ": значения нет, создать и удалить можно");
                        }
                    }
                    catch (Exception ex) { Ok(false, where + ": " + ex.Message); }
                }
            }
        }

        static void Services_()
        {
            foreach (string n in new string[] { "SysMain", "WSearch", "DiagTrack", "MapsBroker", "RetailDemo", "Fax", "RemoteRegistry", "WMPNetworkSvc", "dmwappushservice" })
                Info(n + ": " + (ServiceTools.Exists(n) ? ServiceTools.Status(n) : "нет на этом ПК"));
            if (ServiceTools.Status("SysMain") == "Running")
            {
                ServiceTools.Stop("SysMain");
                string st = ServiceTools.Status("SysMain");
                ServiceTools.Start("SysMain");
                Ok(st == "Stopped" && ServiceTools.Status("SysMain") == "Running", "SysMain: остановить → " + st + ", запустить → " + ServiceTools.Status("SysMain"));
            }
        }

        static void Memory_()
        {
            long sb = MemoryTools.StandbyBytes();
            long freed;
            string err = MemoryTools.Clean(out freed);
            long sa = MemoryTools.StandbyBytes();
            Ok(err == null, "очистка кэша ожидания: " + (err ?? "кэш " + (sb >> 20) + " МБ → " + (sa >> 20) + " МБ, свободно +" + (freed >> 20) + " МБ"));
        }

        static void Net_()
        {
            Ok(NetTools.FlushDns(), "сброс DNS-кэша");
            List<NetAdapterInfo> a = NetTools.EeeAdapters();
            Info("адаптеров с EEE: " + a.Count);
            foreach (NetAdapterInfo x in a)
            {
                Info(x.Name + " *EEE=" + x.Eee + " " + x.Guid);
                object v = Reg.Get("HKLM", x.KeyPath, "*EEE");
                Reg.Set("HKLM", x.KeyPath, "*EEE", Convert.ToString(v), RegistryValueKind.String);
                Ok(true, x.Name + ": запись *EEE разрешена (перезапуск адаптера не проверялся — отключит сеть)");
            }
        }

        static void Priority_()
        {
            ProcessStartInfo si = new ProcessStartInfo("ping.exe", "-n 30 127.0.0.1");
            si.CreateNoWindow = true; si.UseShellExecute = false;
            using (Process p = Process.Start(si))
            {
                ProcessPriorityClass old = p.PriorityClass;
                p.PriorityClass = ProcessPriorityClass.Idle; p.Refresh();
                ProcessPriorityClass now = p.PriorityClass;
                p.PriorityClass = old; p.Refresh();
                Ok(now == ProcessPriorityClass.Idle && p.PriorityClass == old, "приоритет процесса: " + old + " → " + now + " → " + p.PriorityClass);
                p.Kill();
            }
            int n = BackgroundPriority.Lower();
            BackgroundPriority.RestoreAll();
            Info("фоновых процессов понижено и возвращено: " + n);
        }

        static void Pagefile_()
        {
            bool auto = Pagefile.IsAutomatic();
            Pagefile.SetAutomatic(auto);
            Ok(Pagefile.IsAutomatic() == auto, "автоматический файл подкачки = " + auto + ", запись разрешена");
        }
    }
}
