using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using Microsoft.Win32;

namespace Tunex
{
    // ---------------------------------------------------------------- Привилегии процесса
    static class Privileges
    {
        [StructLayout(LayoutKind.Sequential)] struct LUID { public uint Low; public int High; }
        [StructLayout(LayoutKind.Sequential, Pack = 4)] struct TOKEN_PRIVILEGES { public int Count; public LUID Luid; public int Attr; }

        [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr proc, int access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool LookupPrivilegeValue(string sys, string name, out LUID luid);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES state, int len, IntPtr prev, IntPtr retLen);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        public static bool Enable(string name)
        {
            IntPtr token;
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 0x20 | 0x8, out token)) return false;
            try
            {
                LUID luid;
                if (!LookupPrivilegeValue(null, name, out luid)) return false;
                TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES(); tp.Count = 1; tp.Luid = luid; tp.Attr = 2;
                if (!AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero)) return false;
                return Marshal.GetLastWin32Error() == 0; // 1300 = привилегии нет у пользователя
            }
            finally { CloseHandle(token); }
        }
    }

    // ---------------------------------------------------------------- Память
    static class MemoryTools
    {
        [DllImport("ntdll.dll")] static extern int NtSetSystemInformation(int cls, ref int info, int len);
        const int SystemMemoryListInformation = 0x50, FlushModified = 3, PurgeStandby = 4;

        public static long StandbyBytes()
        {
            long sum = 0;
            foreach (string c in new string[] { "Standby Cache Normal Priority Bytes", "Standby Cache Reserve Bytes", "Standby Cache Core Bytes" })
            {
                try { using (PerformanceCounter pc = new PerformanceCounter("Memory", c)) sum += (long)pc.NextValue(); } catch { }
            }
            return sum;
        }

        public static long FreeBytes()
        {
            try { using (PerformanceCounter pc = new PerformanceCounter("Memory", "Free & Zero Page List Bytes")) return (long)pc.NextValue(); }
            catch { return -1; }
        }

        // Очищает «кэш ожидания» и сбрасывает изменённые страницы на диск. Программы не выгружаются.
        // Возвращает null при успехе или текст ошибки.
        public static string Clean(out long freedBytes)
        {
            freedBytes = 0;
            if (!SysInfo.IsAdmin) return "нужны права администратора";
            if (!Privileges.Enable("SeProfileSingleProcessPrivilege")) return "Windows не выдала разрешение на очистку памяти";
            long before = FreeBytes();
            int cmd = FlushModified;
            int st1 = NtSetSystemInformation(SystemMemoryListInformation, ref cmd, 4);
            cmd = PurgeStandby;
            int st2 = NtSetSystemInformation(SystemMemoryListInformation, ref cmd, 4);
            if (st2 != 0) return "Windows вернула код 0x" + st2.ToString("X8");
            long after = FreeBytes();
            if (before >= 0 && after >= 0) freedBytes = Math.Max(0, after - before);
            return null;
        }
    }

    // ---------------------------------------------------------------- Сеть
    class NetAdapterInfo { public string KeyPath, Name, Guid, Eee; }

    static class NetTools
    {
        [DllImport("dnsapi.dll")] static extern bool DnsFlushResolverCache();
        const string ClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

        public static bool FlushDns() { return DnsFlushResolverCache(); }

        // Проводные адаптеры, у которых драйвер поддерживает «Energy Efficient Ethernet»
        public static List<NetAdapterInfo> EeeAdapters()
        {
            List<NetAdapterInfo> list = new List<NetAdapterInfo>();
            using (RegistryKey b = Reg.Base("HKLM"))
            using (RegistryKey cls = b.OpenSubKey(ClassKey))
            {
                if (cls == null) return list;
                foreach (string sub in cls.GetSubKeyNames())
                {
                    if (sub.Length != 4) continue;
                    try
                    {
                        using (RegistryKey k = cls.OpenSubKey(sub))
                        {
                            if (k == null) continue;
                            object eee = k.GetValue("*EEE");
                            if (eee == null) continue;
                            string name = Convert.ToString(k.GetValue("DriverDesc"));
                            if (name.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Hyper-V", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                            NetAdapterInfo a = new NetAdapterInfo();
                            a.KeyPath = ClassKey + "\\" + sub; a.Name = name; a.Guid = Convert.ToString(k.GetValue("NetCfgInstanceId")); a.Eee = Convert.ToString(eee);
                            list.Add(a);
                        }
                    }
                    catch { }
                }
            }
            return list;
        }

        // Перезапуск адаптера, чтобы драйвер перечитал настройки (интернет пропадёт на несколько секунд)
        public static void Restart(string guid)
        {
            using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT * FROM Win32_NetworkAdapter WHERE GUID = '" + guid.Replace("'", "") + "'"))
                foreach (ManagementObject o in s.Get())
                {
                    if (Convert.ToBoolean(o["NetEnabled"] ?? false) || Convert.ToInt32(o["ConfigManagerErrorCode"] ?? 0) == 0)
                    {
                        uint r1 = Convert.ToUInt32(o.InvokeMethod("Disable", null));
                        uint r2 = Convert.ToUInt32(o.InvokeMethod("Enable", null));
                        if (r1 != 0 || r2 != 0) throw new Exception("адаптер не перезапустился (код " + r1 + "/" + r2 + ")");
                    }
                }
        }
    }

    // ---------------------------------------------------------------- Службы
    static class ServiceTools
    {
        public static bool Exists(string name)
        {
            using (RegistryKey b = Reg.Base("HKLM"))
            using (RegistryKey k = b.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name))
                return k != null && k.GetValue("Start") != null;
        }

        public static string Status(string name)
        {
            try { using (ServiceController sc = new ServiceController(name)) return sc.Status.ToString(); } catch { return "нет"; }
        }

        public static void Stop(string name)
        {
            using (ServiceController sc = new ServiceController(name))
            {
                sc.Refresh();
                if (sc.Status == ServiceControllerStatus.Stopped) return;
                if (!sc.CanStop) return;
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
            }
        }

        public static void Start(string name)
        {
            using (ServiceController sc = new ServiceController(name))
            {
                sc.Refresh();
                if (sc.Status == ServiceControllerStatus.Running || sc.Status == ServiceControllerStatus.StartPending) return;
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
            }
        }
    }

    // ---------------------------------------------------------------- Файл подкачки
    static class Pagefile
    {
        static ManagementObject System()
        {
            ManagementObject mo = new ManagementObject(new ManagementScope(@"\\.\root\cimv2"), new ManagementPath("Win32_ComputerSystem.Name='" + Environment.MachineName + "'"), null);
            mo.Scope.Options.EnablePrivileges = true;
            mo.Get();
            return mo;
        }

        public static bool IsAutomatic() { using (ManagementObject mo = System()) return Convert.ToBoolean(mo["AutomaticManagedPagefile"]); }

        public static void SetAutomatic(bool v)
        {
            using (ManagementObject mo = System())
            {
                mo["AutomaticManagedPagefile"] = v;
                mo.Put();
            }
        }
    }

    // ---------------------------------------------------------------- Фоновые процессы во время игры
    static class BackgroundPriority
    {
        public static readonly string[] Names =
        {
            "onedrive.exe", "searchindexer.exe", "searchprotocolhost.exe", "searchfilterhost.exe", "compattelrunner.exe",
            "backgroundtransferhost.exe", "tiworker.exe", "trustedinstaller.exe", "musnotification.exe", "wsappx.exe"
        };

        static readonly Dictionary<int, ProcessPriorityClass> lowered = new Dictionary<int, ProcessPriorityClass>();

        public static bool Active { get { return lowered.Count > 0; } }

        // Понижает приоритет известных фоновых программ. Возвращает, сколько процессов изменено.
        public static int Lower()
        {
            int n = 0;
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    string name = p.ProcessName.ToLowerInvariant() + ".exe";
                    if (Array.IndexOf(Names, name) < 0 || lowered.ContainsKey(p.Id)) continue;
                    ProcessPriorityClass old = p.PriorityClass;
                    if (old == ProcessPriorityClass.Idle) continue;
                    p.PriorityClass = ProcessPriorityClass.Idle;
                    lowered[p.Id] = old; n++;
                }
                catch { } // защищённые процессы Windows не даёт менять — это нормально
                finally { p.Dispose(); }
            }
            return n;
        }

        public static void RestoreAll()
        {
            foreach (KeyValuePair<int, ProcessPriorityClass> kv in lowered)
            {
                try { using (Process p = Process.GetProcessById(kv.Key)) p.PriorityClass = kv.Value; } catch { }
            }
            lowered.Clear();
        }

        // Какая игра из списка сейчас запущена (или null)
        public static string RunningGame(IList<string> games)
        {
            if (games.Count == 0) return null;
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string g in games) names.Add(Path.GetFileNameWithoutExtension(g));
            foreach (Process p in Process.GetProcesses())
            {
                try { if (names.Contains(p.ProcessName)) return p.ProcessName; }
                catch { }
                finally { p.Dispose(); }
            }
            return null;
        }
    }
}
