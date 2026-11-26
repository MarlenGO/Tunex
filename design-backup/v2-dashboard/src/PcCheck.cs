using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Management;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Tunex
{
    // ---------------------------------------------------------------- Подробные характеристики ПК (для вкладки «Оценка ПК»)
    class GpuInfo
    {
        public string Name, DriverVersion;
        public DateTime? DriverDate;
        public int Score; public bool Integrated, Known;
    }

    class MemInfo
    {
        public long Capacity; public int Speed, Configured, Type, FormFactor;
        public string Locator = "", Bank = "", Maker = "";
        public string TypeName
        {
            get
            {
                switch (Type)
                {
                    case 20: return "DDR"; case 21: return "DDR2"; case 24: return "DDR3";
                    case 26: return "DDR4"; case 34: return "DDR5"; case 35: return "LPDDR5";
                    case 29: return "LPDDR3"; case 30: return "LPDDR4"; default: return "";
                }
            }
        }
        // Канал по названию слота: «Controller1-ChannelA-DIMM0», «DIMM_A1», «ChannelB-DIMM1»…; null — не понять
        public string ChannelKey
        {
            get
            {
                string s = (Locator + " " + Bank).ToUpperInvariant();
                Match ctrl = Regex.Match(s, "CONTROLLER\\s*(\\d)");
                Match ch = Regex.Match(s, "CHANNEL\\s*([A-H])(?![A-Z])");
                if (!ch.Success) ch = Regex.Match(s, "DIMM[_ ]?([A-H])\\d");
                if (!ch.Success) return null;
                return (ctrl.Success ? ctrl.Groups[1].Value : "0") + ch.Groups[1].Value;
            }
        }
    }

    class DiskInfo
    {
        public string Name; public long Size; public bool System, Ssd, Hdd, Nvme, Usb;
        public long Free = -1;
        public string Kind { get { return Usb ? "USB-диск" : Nvme ? "NVMe SSD" : Ssd ? "SSD" : Hdd ? "HDD (жёсткий диск)" : "Диск"; } }
    }

    class PcSpecs
    {
        public string Cpu; public int Cores, Threads, BaseMHz;
        public List<GpuInfo> Gpus = new List<GpuInfo>();
        public List<MemInfo> Ram = new List<MemInfo>();
        public List<DiskInfo> Disks = new List<DiskInfo>();
        public List<MonitorInfo> Monitors = new List<MonitorInfo>();
        public bool Laptop, OnBattery, SaverPlan;
        public long RamTotal { get { long t = 0; foreach (MemInfo m in Ram) t += m.Capacity; return t; } }
        public bool HasDiscrete { get { foreach (GpuInfo g in Gpus) if (!g.Integrated) return true; return false; } }
        public bool HasIntegrated { get { foreach (GpuInfo g in Gpus) if (g.Integrated) return true; return false; } }
    }

    enum Severity { Critical, Warning, Info }

    class Finding
    {
        public Severity Level;
        public string Glyph, Title, Text, Button;
        public Action Fix;
    }

    static class PcCheck
    {
        // ---- Сбор (WMI медленный — вызывать в фоне)
        public static PcSpecs Collect()
        {
            PcSpecs s = new PcSpecs();
            s.Cpu = SysInfo.CpuName;
            Wmi("SELECT NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor", delegate(ManagementObject o)
            {
                s.Cores += Int(o["NumberOfCores"]); s.Threads += Int(o["NumberOfLogicalProcessors"]);
                s.BaseMHz = Math.Max(s.BaseMHz, Int(o["MaxClockSpeed"]));
            });
            if (s.Threads == 0) s.Threads = Environment.ProcessorCount;

            Wmi("SELECT Name, DriverVersion, DriverDate FROM Win32_VideoController", delegate(ManagementObject o)
            {
                string n = Convert.ToString(o["Name"]);
                if (n.IndexOf("Basic Display", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Remote", StringComparison.OrdinalIgnoreCase) >= 0) return;
                GpuInfo g = new GpuInfo(); g.Name = n; g.DriverVersion = Convert.ToString(o["DriverVersion"]);
                try { string d = Convert.ToString(o["DriverDate"]); if (!string.IsNullOrEmpty(d)) g.DriverDate = ManagementDateTimeConverter.ToDateTime(d); } catch { }
                bool ig; int sc = GpuDb.ScoreOf(n, out ig);
                g.Known = sc >= 0; g.Integrated = ig; g.Score = sc >= 0 ? sc : 0;
                if (sc < 0) { bool k; g.Score = GpuDb.Score(out k, out ig); g.Integrated = ig; }
                s.Gpus.Add(g);
            });

            Wmi("SELECT Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType, FormFactor, DeviceLocator, BankLabel, Manufacturer FROM Win32_PhysicalMemory", delegate(ManagementObject o)
            {
                MemInfo m = new MemInfo();
                try { m.Capacity = Convert.ToInt64(o["Capacity"]); } catch { }
                m.Speed = Int(o["Speed"]); m.Configured = Int(o["ConfiguredClockSpeed"]);
                m.Type = Int(o["SMBIOSMemoryType"]); m.FormFactor = Int(o["FormFactor"]);
                m.Locator = Convert.ToString(o["DeviceLocator"]) ?? ""; m.Bank = Convert.ToString(o["BankLabel"]) ?? "";
                m.Maker = (Convert.ToString(o["Manufacturer"]) ?? "").Trim();
                s.Ram.Add(m);
            });

            // Диски: MSFT_PhysicalDisk знает тип (SSD/HDD) и шину; номер системного диска — по разделу C:
            string sysDisk = null;
            string sysLetter = Path.GetPathRoot(Environment.SystemDirectory).Substring(0, 1);
            WmiNs(@"root\Microsoft\Windows\Storage", "SELECT DriveLetter, DiskNumber FROM MSFT_Partition", delegate(ManagementObject o)
            {
                object dl = o["DriveLetter"];
                if (dl != null && Convert.ToChar(dl).ToString().Equals(sysLetter, StringComparison.OrdinalIgnoreCase)) sysDisk = Convert.ToString(o["DiskNumber"]);
            });
            WmiNs(@"root\Microsoft\Windows\Storage", "SELECT FriendlyName, MediaType, BusType, Size, DeviceId, SpindleSpeed FROM MSFT_PhysicalDisk", delegate(ManagementObject o)
            {
                DiskInfo d = new DiskInfo();
                d.Name = (Convert.ToString(o["FriendlyName"]) ?? "").Trim();
                try { d.Size = Convert.ToInt64(o["Size"]); } catch { }
                int media = Int(o["MediaType"]), bus = Int(o["BusType"]);
                long spindle = 0; try { spindle = Convert.ToInt64(o["SpindleSpeed"]); } catch { }
                d.Usb = bus == 7;
                d.Nvme = bus == 17 || d.Name.IndexOf("NVMe", StringComparison.OrdinalIgnoreCase) >= 0;
                d.Ssd = media == 4 || d.Nvme || (media == 0 && d.Name.IndexOf("SSD", StringComparison.OrdinalIgnoreCase) >= 0);
                d.Hdd = !d.Ssd && (media == 3 || (spindle > 0 && spindle < uint.MaxValue));
                d.System = sysDisk != null && Convert.ToString(o["DeviceId"]) == sysDisk;
                s.Disks.Add(d);
            });
            try
            {
                DriveInfo c = new DriveInfo(sysLetter);
                foreach (DiskInfo d in s.Disks) if (d.System) d.Free = c.AvailableFreeSpace;
                if (s.Disks.Count == 0) { DiskInfo d = new DiskInfo(); d.Name = "Системный диск " + sysLetter + ":"; d.Size = c.TotalSize; d.Free = c.AvailableFreeSpace; d.System = true; s.Disks.Add(d); }
            }
            catch { }

            try { s.Monitors = Displays.Enumerate(false); } catch { }

            // Ноутбук: есть батарея или корпус «портативный»
            Wmi("SELECT BatteryStatus FROM Win32_Battery", delegate(ManagementObject o) { s.Laptop = true; });
            Wmi("SELECT ChassisTypes FROM Win32_SystemEnclosure", delegate(ManagementObject o)
            {
                ushort[] t = o["ChassisTypes"] as ushort[];
                if (t != null) foreach (ushort c in t) if (c == 8 || c == 9 || c == 10 || c == 14 || c == 31 || c == 32) s.Laptop = true;
            });
            s.OnBattery = s.Laptop && SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline;
            try { s.SaverPlan = Power.Active() == Power.Saver; } catch { }
            return s;
        }

        static int Int(object v) { try { return v == null ? 0 : Convert.ToInt32(v); } catch { return 0; } }

        static void Wmi(string q, Action<ManagementObject> each) { WmiNs(@"root\cimv2", q, each); }

        static void WmiNs(string ns, string q, Action<ManagementObject> each)
        {
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(ns, q))
                    foreach (ManagementObject o in s.Get()) using (o) { try { each(o); } catch { } }
            }
            catch { }
        }

        public static string Gb(long bytes) { double g = bytes / 1073741824.0; return g >= 100 ? g.ToString("0") + " ГБ" : g.ToString(g < 10 ? "0.#" : "0") + " ГБ"; }

        // ---- Узкие места: только то, что можно определить надёжно
        public static List<Finding> Analyze(PcSpecs s)
        {
            List<Finding> f = new List<Finding>();
            bool igpuOnly = s.Gpus.Count > 0 && !s.HasDiscrete;

            // Память: объём
            long total = s.RamTotal;
            if (total > 0 && total < 7L * 1073741824)
                Add(f, Severity.Critical, Icons.Memory, "Мало оперативной памяти: " + Gb(total),
                    "Современным играм нужно 8–16 ГБ. Когда памяти не хватает, Windows сбрасывает данные на диск, и игра начинает зависать. Решение — добавить память.");
            else if (total > 0 && total < 15L * 1073741824)
                Add(f, Severity.Warning, Icons.Memory, "Оперативной памяти " + Gb(total) + " — впритык",
                    "Для большинства новых игр желательно 16 ГБ. Перед игрой закрывайте браузер и лишние программы.");

            // Память: каналы
            if (s.Ram.Count == 1)
                Add(f, igpuOnly ? Severity.Critical : Severity.Warning, Icons.Memory, "Память работает в одноканальном режиме",
                    "Установлена одна планка (" + Gb(s.Ram[0].Capacity) + "). Процессор получает данные примерно вдвое медленнее" +
                    (igpuOnly ? ", а встроенная видеокарта теряет до 30–40% FPS — для неё это главное ограничение." : ", в играх это даёт меньше FPS и больше просадок.") +
                    " Решение — вторая такая же планка (тот же объём и частота)." + (s.Laptop ? " В некоторых ноутбуках память распаяна на плате — тогда добавить нельзя." : ""));
            else if (s.Ram.Count >= 2)
            {
                bool mixed = false;
                foreach (MemInfo m in s.Ram) if (m.Capacity != s.Ram[0].Capacity) mixed = true;
                // Все планки в одном канале — частая ошибка сборки (слоты A1 и A2 вместо A2 и B2)
                string key0 = s.Ram[0].ChannelKey; bool allSame = key0 != null;
                foreach (MemInfo m in s.Ram) if (m.ChannelKey != key0) allSame = false;
                if (allSame && !s.Laptop)
                    Add(f, Severity.Critical, Icons.Memory, "Все планки памяти стоят в одном канале",
                        "Планки установлены в слоты одного канала (" + s.Ram[0].Locator + ", " + s.Ram[1].Locator + "), поэтому память работает как одноканальная. " +
                        "Переставьте их через один слот — обычно это A2 и B2, точная схема есть в инструкции к материнской плате.");
                else if (mixed)
                    Add(f, Severity.Info, Icons.Memory, "Планки памяти разного объёма",
                        "Часть памяти может работать в одноканальном режиме. Это не страшно, но две одинаковые планки работают быстрее.");
            }

            // Память: частота ниже паспортной
            int rated = 0, actual = int.MaxValue; string type = "";
            foreach (MemInfo m in s.Ram) { rated = Math.Max(rated, m.Speed); if (m.Configured > 0) actual = Math.Min(actual, m.Configured); if (m.TypeName != "") type = m.TypeName; }
            if (actual == int.MaxValue) actual = 0;
            if (rated > 0 && actual > 0 && actual < rated * 0.95)
                Add(f, s.Laptop ? Severity.Info : Severity.Warning, Icons.Memory, "Память работает на " + actual + " МГц вместо " + rated,
                    s.Laptop ? "Скорее всего, частоту ограничил производитель ноутбука — тогда изменить её нельзя."
                             : "Обычно это значит, что в BIOS не включён профиль памяти XMP (Intel) или EXPO (AMD). Включение даёт 5–15% FPS в играх, которые зависят от процессора.");
            else if (!s.Laptop && actual > 0 && ((type == "DDR4" && actual <= 2400) || (type == "DDR5" && actual <= 4800)))
                Add(f, Severity.Info, Icons.Memory, "Память на базовой частоте " + actual + " МГц",
                    "Если планки продавались как более быстрые (например, DDR4-3200 или DDR5-6000), в BIOS нужно включить профиль XMP или EXPO. Windows видит только базовую частоту, поэтому проверить это точно нельзя.");

            // Диски
            foreach (DiskInfo d in s.Disks)
            {
                if (d.System && d.Hdd)
                    Add(f, Severity.Critical, Icons.System, "Windows стоит на жёстком диске (HDD)",
                        "Жёсткий диск в 20–50 раз медленнее SSD: долгая загрузка игр, подвисания при подгрузке карты. Перенос Windows и игр на SSD — самое заметное улучшение для такого ПК.");
                else if (!d.System && d.Hdd)
                    Add(f, Severity.Info, Icons.System, "Жёсткий диск: " + d.Name,
                        "Хорош для фильмов и фото, но игры на нём будут долго загружаться. Ставьте игры на SSD.");
                if (d.System && d.Free >= 0 && (d.Free < 20L * 1073741824 || (d.Size > 0 && d.Free < d.Size / 10)))
                    Add(f, Severity.Warning, Icons.System, "На системном диске мало места: " + Gb(d.Free),
                        "Windows и играм нужно место для обновлений, кэша шейдеров и файла подкачки. Когда его мало, бывают фризы и ошибки обновлений. Освободите хотя бы 20–30 ГБ.");
            }

            // Мониторы: частота ниже максимальной
            foreach (MonitorInfo m in s.Monitors)
            {
                int max = m.MaxHz;
                if (max > m.Hz + 5 && m.Hz > 0)
                {
                    MonitorInfo mm = m;
                    Finding x = Add(f, Severity.Critical, Icons.Display, "Монитор работает на " + m.Hz + " Гц, а может " + max + " Гц",
                        "Картинка в играх менее плавная, чем могла бы быть, даже если FPS высокий. Частая ошибка после подключения монитора или обновления драйвера.");
                    x.Button = "Включить " + max + " Гц";
                    x.Fix = delegate
                    {
                        Tweak t = Catalog.Resolve("display.hz|" + mm.Device);
                        string err = App.Manager.ApplyAction(t, "Частота «" + mm.Name + "»: " + max + " Гц", delegate { Displays.SetHz(mm.Device, max); });
                        if (err != null) App.Error(err); else App.Toast("Частота экрана: " + max + " Гц. Отменить можно в «Журнале».", Theme.Green);
                    };
                }
            }

            // Видеокарты
            if (igpuOnly)
                Add(f, Severity.Info, Icons.Gpu, "Нет отдельной видеокарты",
                    "Встроенная графика (" + s.Gpus[0].Name + ") тянет лёгкие игры — CS2, Dota 2, Minecraft, Valorant — на низких настройках. Новые требовательные игры пойдут плохо или не запустятся.");
            if (s.HasDiscrete && s.HasIntegrated && s.Laptop)
            {
                Finding x = Add(f, Severity.Info, Icons.Gpu, "В ноутбуке две видеокарты",
                    "Windows сама решает, на какой запускать программу, и иногда запускает игру на слабой встроенной. Добавьте игры во вкладке «Игры» и включите для них «Мощная видеокарта».");
                x.Button = "Открыть «Игры»"; x.Fix = delegate { App.Form.Navigate("games"); };
            }
            foreach (GpuInfo g in s.Gpus)
            {
                if (g.DriverDate == null) continue;
                double months = (DateTime.Now - g.DriverDate.Value).TotalDays / 30.4;
                bool discrete = !g.Integrated;
                if ((discrete && months > 6) || (!discrete && months > 12))
                {
                    Finding x = Add(f, discrete ? Severity.Warning : Severity.Info, Icons.Gpu,
                        "Драйвер видеокарты от " + g.DriverDate.Value.ToString("dd.MM.yyyy"),
                        g.Name + ": драйверу " + Age(months) + ". В новых драйверах исправляют фризы и ускоряют свежие игры." +
                        (discrete ? "" : " Для встроенной графики ноутбука драйвер иногда выпускает только производитель ноутбука — смотрите на его сайте."));
                    string url = DriverUrl(g.Name);
                    if (url != null) { x.Button = "Сайт драйверов"; x.Fix = delegate { try { System.Diagnostics.Process.Start(url); } catch (Exception ex) { App.Error(ex.Message); } }; }
                }
            }

            // Питание
            if (s.OnBattery)
                Add(f, Severity.Warning, Icons.Power, "Ноутбук работает от батареи",
                    "На батарее Windows и производитель ограничивают процессор и видеокарту — FPS может быть вдвое ниже. Для игр подключите зарядку.");
            if (s.SaverPlan)
            {
                Finding x = Add(f, Severity.Warning, Icons.Power, "Включена схема питания «Экономия энергии»",
                    "Процессор специально работает медленнее. Для игр выберите другую схему.");
                x.Button = "Открыть"; x.Fix = delegate { App.Form.Navigate("power"); };
            }

            // Процессор
            if (s.Threads > 0 && s.Threads < 4)
                Add(f, Severity.Warning, Icons.Cpu, "Процессор: всего " + s.Threads + " потока",
                    "Многие современные игры рассчитаны минимум на 4–6 потоков — возможны рывки, особенно в онлайн-играх.");

            f.Sort(delegate(Finding a, Finding b) { return ((int)a.Level).CompareTo((int)b.Level); });
            return f;
        }

        static Finding Add(List<Finding> l, Severity lv, string glyph, string title, string text)
        {
            Finding x = new Finding(); x.Level = lv; x.Glyph = glyph; x.Title = title; x.Text = text; l.Add(x); return x;
        }

        static string Age(double months)
        {
            if (months < 12) return ((int)months) + " мес.";
            double y = months / 12;
            return y < 2 ? "больше года" : "около " + Math.Round(y) + (Math.Round(y) < 5 ? " лет".Replace(" лет", " года") : " лет");
        }

        static string DriverUrl(string gpu)
        {
            if (gpu.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0) return "https://www.nvidia.com/Download/index.aspx";
            if (gpu.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0 || gpu.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0) return "https://www.amd.com/en/support/download/drivers.html";
            if (gpu.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0) return "https://www.intel.com/content/www/us/en/support/detect.html";
            return null;
        }
    }
}
