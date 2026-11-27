using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Tunex
{
    // ================================================================ Мышь: настоящая частота опроса по сырому вводу (Raw Input)
    class MouseRate
    {
        readonly List<long> stamps = new List<long>();   // время каждого сообщения о движении, в тиках Stopwatch
        public int Events { get { return stamps.Count; } }

        public void Add(long ticks) { stamps.Add(ticks); }
        public void Clear() { stamps.Clear(); }

        // Частота по отрезкам 100 мс, где мышь двигалась без остановки; берём верхнюю четверть — когда вели быстро
        public int Measured()
        {
            List<float> rates = Buckets();
            if (rates.Count < 5) return -1;
            rates.Sort();
            return (int)rates[(int)(rates.Count * 0.75f)];
        }

        List<float> Buckets()
        {
            List<float> r = new List<float>();
            if (stamps.Count < 20) return r;
            long bucket = Stopwatch.Frequency / 10;
            long start = stamps[0]; int n = 0;
            for (int i = 1; i < stamps.Count; i++)
            {
                // перерыв больше 50 мс — мышь остановилась, отрезок не считаем
                if (stamps[i] - stamps[i - 1] > Stopwatch.Frequency / 20) { start = stamps[i]; n = 0; continue; }
                n++;
                if (stamps[i] - start >= bucket) { r.Add(n * (float)Stopwatch.Frequency / (stamps[i] - start)); start = stamps[i]; n = 0; }
            }
            return r;
        }

        // Ровность: доля отрезков, где частота не проседала ниже 70% от обычной
        public int Steady()
        {
            int hz = Measured(); if (hz <= 0) return -1;
            List<float> rates = Buckets(); int ok = 0;
            foreach (float f in rates) if (f >= hz * 0.7f) ok++;
            return rates.Count > 0 ? ok * 100 / rates.Count : -1;
        }

        public static int Nominal(int hz)
        {
            int[] std = { 125, 250, 500, 1000, 2000, 4000, 8000 };
            int best = std[0];
            foreach (int s in std) if (Math.Abs(Math.Log((double)hz / s)) < Math.Abs(Math.Log((double)hz / best))) best = s;
            return best;
        }

        public static string Verdict(int hz, out string text, out System.Drawing.Color c)
        {
            int nom = Nominal(hz);
            if (nom <= 125)
            {
                c = Theme.Orange;
                text = "Мышь сообщает о движении 125 раз в секунду — это обычная офисная частота, курсор обновляется раз в 8 мс. " +
                    "Если мышь игровая, включите 1000 Гц в её программе (Logitech G HUB, Razer Synapse, Bloody, SteelSeries GG) или кнопкой снизу мыши. " +
                    "Беспроводные мыши через Bluetooth обычно работают только на 125 Гц — для игр лучше подключить их через радиоприёмник или кабель.";
                return "Мышь работает на 125 Гц";
            }
            if (nom <= 250)
            {
                c = Theme.Orange;
                text = "Неплохо, но игровые мыши умеют 1000 Гц: курсор будет обновляться в 4 раза чаще. Включите это в программе мыши.";
                return "Мышь работает на 250 Гц";
            }
            c = Theme.Green;
            text = nom >= 2000 ? "Очень высокая частота. Если в играх бывают подлагивания при резких движениях — попробуйте 1000 Гц: не все игры и процессоры успевают обрабатывать столько сообщений."
                : nom >= 1000 ? "Отлично: курсор обновляется каждую миллисекунду, как и нужно для игр." : "Хорошо для игр. Если мышь умеет 1000 Гц, можно включить в её программе.";
            return "Мышь работает на " + nom + " Гц";
        }

        // ---- WinAPI
        [StructLayout(LayoutKind.Sequential)]
        struct RAWINPUTDEVICE { public ushort UsagePage, Usage; public uint Flags; public IntPtr Target; }
        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] d, uint n, uint size);
        [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr h, uint cmd, IntPtr data, ref uint size, uint headerSize);

        public const int WM_INPUT = 0x00FF;

        public static bool Register(IntPtr hwnd, bool on)
        {
            RAWINPUTDEVICE d = new RAWINPUTDEVICE();
            d.UsagePage = 1; d.Usage = 2;                                       // мышь
            d.Flags = on ? 0x100u : 0x1u;                                       // RIDEV_INPUTSINK / RIDEV_REMOVE
            d.Target = on ? hwnd : IntPtr.Zero;
            return RegisterRawInputDevices(new[] { d }, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
        }

        // true, если в сообщении есть движение (не только кнопки)
        public static bool IsMove(IntPtr lParam)
        {
            uint size = 0, hdr = (uint)(8 + IntPtr.Size * 2);
            GetRawInputData(lParam, 0x10000003, IntPtr.Zero, ref size, hdr);   // RID_INPUT
            if (size == 0 || size > 256) return false;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetRawInputData(lParam, 0x10000003, buf, ref size, hdr) != size) return false;
                if (Marshal.ReadInt32(buf, 0) != 0) return false;               // RIM_TYPEMOUSE
                int o = (int)hdr;                                               // RAWMOUSE: usFlags(2) pad(2) buttons(4) raw(4) lLastX lLastY
                int dx = Marshal.ReadInt32(buf, o + 12), dy = Marshal.ReadInt32(buf, o + 16);
                return dx != 0 || dy != 0;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
    }

    // ================================================================ Место на диске: только то, что Windows и программы создадут заново
    class CleanItem
    {
        public string Key, Title, Desc;
        public bool Admin, DefaultOn;
        public int MinAgeHours;                       // файлы моложе не трогать: ими может пользоваться работающая программа
        public readonly List<string> Dirs = new List<string>();
        public readonly List<string> Files = new List<string>();
        public string Blocked;                        // почему сейчас нельзя (например, открыт Steam)
        public long Size; public int Count;
    }

    static class Cleaner
    {
        public static List<CleanItem> Items()
        {
            List<CleanItem> l = new List<CleanItem>();
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            CleanItem t = Item(l, "temp", "Временные файлы", "Программы и установщики оставляют их и забывают удалить. Файлы моложе недели не трогаются — как во встроенной «Очистке диска» Windows.", false, true, 168);
            t.Dirs.Add(Path.GetTempPath());
            if (SysInfo.IsAdmin) t.Dirs.Add(Path.Combine(win, "Temp"));

            CleanItem wu = Item(l, "updates", "Скачанные обновления Windows", "Уже установленные обновления, которые Windows держит про запас. Если какое-то понадобится, Windows скачает его снова.", true, true, 48);
            wu.Dirs.Add(Path.Combine(win, @"SoftwareDistribution\Download"));
            wu.Dirs.Add(Path.Combine(win, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"));

            CleanItem d = Item(l, "dumps", "Отчёты о сбоях", "Снимки памяти после вылетов программ и синих экранов. Нужны, только если вы отправляете их разработчикам.", false, true, 0);
            d.Dirs.Add(Path.Combine(local, "CrashDumps"));
            if (SysInfo.IsAdmin) { d.Dirs.Add(Path.Combine(win, "Minidump")); d.Dirs.Add(Path.Combine(win, "LiveKernelReports")); d.Files.Add(Path.Combine(win, "MEMORY.DMP")); }

            CleanItem s = Item(l, "steam", "Остатки загрузок Steam", "Недокачанные и брошенные обновления игр в папках downloading и temp. Чистится, только когда Steam закрыт.", false, true, 0);
            foreach (string lib in SteamLib.Libraries()) { s.Dirs.Add(Path.Combine(lib, @"steamapps\downloading")); s.Dirs.Add(Path.Combine(lib, @"steamapps\temp")); }
            if (Process.GetProcessesByName("steam").Length > 0) s.Blocked = "Steam открыт — закройте его, чтобы не сломать идущую загрузку.";

            CleanItem sh = Item(l, "shaders", "Кэш шейдеров видеокарты", "Игры соберут его заново: первые минуты в каждой игре могут подтормаживать. Чистите, только если игры начали глючить после обновления драйвера.", false, false, 0);
            foreach (string p in new[] { "D3DSCache", @"NVIDIA\DXCache", @"NVIDIA\GLCache", @"AMD\DxCache", @"AMD\DxcCache", @"AMD\GLCache", @"Intel\ShaderCache" }) sh.Dirs.Add(Path.Combine(local, p));

            l.RemoveAll(delegate(CleanItem x) { return x.Admin && !SysInfo.IsAdmin; });
            return l;
        }

        static CleanItem Item(List<CleanItem> l, string key, string title, string desc, bool admin, bool on, int age)
        {
            CleanItem i = new CleanItem(); i.Key = key; i.Title = title; i.Desc = desc; i.Admin = admin; i.DefaultOn = on; i.MinAgeHours = age;
            l.Add(i); return i;
        }

        public static void Measure(CleanItem i)
        {
            i.Size = 0; i.Count = 0;
            Walk(i, false);
        }

        // Удаляет и возвращает, сколько освободилось; занятые файлы пропускаются
        public static long Clean(CleanItem i)
        {
            if (i.Blocked != null) return 0;
            i.Size = 0; i.Count = 0;
            long freed = Walk(i, true);
            Measure(i);
            return freed;
        }

        static long Walk(CleanItem i, bool delete)
        {
            DateTime limit = DateTime.Now.AddHours(-i.MinAgeHours);
            long freed = 0;
            foreach (string f in i.Files) freed += File1(i, new FileInfo(f), limit, delete);
            foreach (string d in i.Dirs)
            {
                DirectoryInfo di = new DirectoryInfo(d);
                if (!di.Exists || (di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                freed += Dir(i, di, limit, delete, 0);
            }
            return freed;
        }

        static long Dir(CleanItem i, DirectoryInfo di, DateTime limit, bool delete, int depth)
        {
            long freed = 0;
            if (depth > 20) return 0;
            try { foreach (FileInfo f in di.GetFiles()) freed += File1(i, f, limit, delete); } catch { }
            try
            {
                foreach (DirectoryInfo sub in di.GetDirectories())
                {
                    if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) continue;   // ссылки на другие папки не трогаем
                    freed += Dir(i, sub, limit, delete, depth + 1);
                    if (delete) { try { if (sub.GetFileSystemInfos().Length == 0) sub.Delete(); } catch { } }
                }
            }
            catch { }
            return freed;
        }

        static long File1(CleanItem i, FileInfo f, DateTime limit, bool delete)
        {
            try
            {
                if (!f.Exists || f.LastWriteTime > limit) return 0;
                long len = f.Length;
                if (!delete) { i.Size += len; i.Count++; return 0; }
                if ((f.Attributes & FileAttributes.ReadOnly) != 0) f.Attributes &= ~FileAttributes.ReadOnly;
                f.Delete();
                return len;
            }
            catch { return 0; }
        }

        public static bool Picked(CleanItem i)
        {
            AppState s = App.Manager.State;
            if (s.CleanPick.Contains(i.Key)) return true;
            if (s.CleanSkip.Contains(i.Key)) return false;
            return i.DefaultOn;
        }

        public static void SetPicked(CleanItem i, bool on)
        {
            AppState s = App.Manager.State;
            s.CleanPick.Remove(i.Key); s.CleanSkip.Remove(i.Key);
            if (on != i.DefaultOn) (on ? s.CleanPick : s.CleanSkip).Add(i.Key);
            s.Save();
        }
    }

    // ================================================================ Автозагрузка: что запускается с Windows и сколько это стоит
    class StartupItem
    {
        public string Name, Command, Exe, Source;     // Source — где записано: для показа
        public string Root, ApprovedPath, ValueName;  // где лежит отметка «включено/выключено» (как в Диспетчере задач)
        public bool Enabled, NeedsAdmin;
        public float SlowSec = -1;                     // насколько Windows замечала замедление загрузки из-за программы
        public int SlowCount;
    }

    class BootInfo { public DateTime When; public float Seconds, MainSeconds; }

    static class Startup
    {
        const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

        public static List<StartupItem> List()
        {
            List<StartupItem> l = new List<StartupItem>();
            FromRun(l, "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run", "Run", "при входе в вашу учётную запись");
            FromRun(l, "HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "Run", "для всех пользователей ПК");
            FromRun(l, "HKLM", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Run32", "для всех пользователей ПК");
            FromFolder(l, "HKCU", Environment.GetFolderPath(Environment.SpecialFolder.Startup), "из папки «Автозагрузка»");
            FromFolder(l, "HKLM", Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "из общей папки «Автозагрузка»");
            l.Sort(delegate(StartupItem a, StartupItem b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); });
            return l;
        }

        static void FromRun(List<StartupItem> l, string root, string path, string approved, string source)
        {
            try
            {
                using (RegistryKey b = Reg.Base(root))
                using (RegistryKey k = b.OpenSubKey(path))
                {
                    if (k == null) return;
                    foreach (string n in k.GetValueNames())
                    {
                        if (n == "") continue;
                        StartupItem i = new StartupItem();
                        i.ValueName = n; i.Command = Convert.ToString(k.GetValue(n)); i.Exe = ExeOf(i.Command);
                        i.Root = root; i.ApprovedPath = Approved + approved; i.Source = source; i.NeedsAdmin = root == "HKLM";
                        i.Name = Friendly(i.Exe, n);
                        i.Enabled = IsOn(root, i.ApprovedPath, n);
                        l.Add(i);
                    }
                }
            }
            catch { }
        }

        static void FromFolder(List<StartupItem> l, string root, string dir, string source)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                foreach (string f in Directory.GetFiles(dir))
                {
                    string fn = Path.GetFileName(f);
                    if (fn.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    StartupItem i = new StartupItem();
                    i.ValueName = fn; i.Command = f; i.Exe = f;
                    i.Root = root; i.ApprovedPath = Approved + "StartupFolder"; i.Source = source; i.NeedsAdmin = root == "HKLM";
                    i.Name = Path.GetFileNameWithoutExtension(fn);
                    i.Enabled = IsOn(root, i.ApprovedPath, fn);
                    l.Add(i);
                }
            }
            catch { }
        }

        // Отметка Диспетчера задач: первый байт чётный — включено, нечётный — выключено; нет отметки — включено
        static bool IsOn(string root, string path, string name)
        {
            byte[] v = Reg.Get(root, path, name) as byte[];
            return v == null || v.Length == 0 || (v[0] & 1) == 0;
        }

        public static string SetEnabled(StartupItem i, bool on)
        {
            if (i.NeedsAdmin && !SysInfo.IsAdmin) return "Нужны права администратора: эта программа запускается для всех пользователей.";
            try
            {
                byte[] v = new byte[12];
                v[0] = (byte)(on ? 2 : 3);
                if (!on) BitConverter.GetBytes(DateTime.Now.ToFileTime()).CopyTo(v, 4);
                Reg.Set(i.Root, i.ApprovedPath, i.ValueName, v, RegistryValueKind.Binary);
                i.Enabled = on;
                return null;
            }
            catch (Exception e) { return e.Message; }
        }

        public static string ExeOf(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return "";
            cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
            if (cmd.StartsWith("\"")) { int q = cmd.IndexOf('"', 1); return q > 1 ? cmd.Substring(1, q - 1) : cmd.Trim('"'); }
            Match m = Regex.Match(cmd, @"^(.+?\.(exe|cmd|bat|lnk))(\s|$)", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : cmd.Split(' ')[0];
        }

        static string Friendly(string exe, string fallback)
        {
            try
            {
                if (File.Exists(exe))
                {
                    FileVersionInfo fv = FileVersionInfo.GetVersionInfo(exe);
                    string d = !string.IsNullOrEmpty(fv.FileDescription) && fv.FileDescription.Trim().Length > 2 ? fv.FileDescription.Trim() : fv.ProductName;
                    if (!string.IsNullOrEmpty(d) && d.Trim().Length > 2) return d.Trim();
                }
            }
            catch { }
            return fallback;
        }

        // ---- Журнал «Diagnostics-Performance»: время загрузки (событие 100) и программы, замедлившие её (событие 101)
        const string Log = "Microsoft-Windows-Diagnostics-Performance/Operational";

        public static List<BootInfo> Boots(int max)
        {
            List<BootInfo> l = new List<BootInfo>();
            try
            {
                EventLogQuery q = new EventLogQuery(Log, PathType.LogName, "*[System[(EventID=100)]]"); q.ReverseDirection = true;
                using (EventLogReader r = new EventLogReader(q))
                    for (EventRecord e = r.ReadEvent(); e != null && l.Count < max; e = r.ReadEvent())
                        using (e)
                        {
                            Dictionary<string, string> d = Data(e);
                            BootInfo b = new BootInfo(); b.When = e.TimeCreated ?? DateTime.MinValue;
                            b.Seconds = Ms(d, "BootTime") / 1000f; b.MainSeconds = Ms(d, "MainPathBootTime") / 1000f;
                            if (b.Seconds > 0) l.Add(b);
                        }
            }
            catch { }
            return l;
        }

        public static string BootError;

        // Средние замедления по имени exe за последние 60 дней
        public static void AttachSlow(List<StartupItem> items)
        {
            BootError = null;
            Dictionary<string, float> sum = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> cnt = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                EventLogQuery q = new EventLogQuery(Log, PathType.LogName, "*[System[(EventID=101) and TimeCreated[timediff(@SystemTime) <= 5184000000]]]");
                using (EventLogReader r = new EventLogReader(q))
                    for (EventRecord e = r.ReadEvent(); e != null; e = r.ReadEvent())
                        using (e)
                        {
                            Dictionary<string, string> d = Data(e);
                            string n; if (!d.TryGetValue("Name", out n) || string.IsNullOrEmpty(n)) continue;
                            float s = Ms(d, "DegradationTime") / 1000f;
                            float v; sum.TryGetValue(n, out v); sum[n] = v + s;
                            int c; cnt.TryGetValue(n, out c); cnt[n] = c + 1;
                        }
            }
            catch (UnauthorizedAccessException) { BootError = "Журнал загрузки Windows доступен только с правами администратора."; }
            catch (Exception e) { BootError = e.Message; }
            foreach (StartupItem i in items)
            {
                string fn = Path.GetFileName(i.Exe ?? "");
                if (fn != "" && cnt.ContainsKey(fn)) { i.SlowCount = cnt[fn]; i.SlowSec = sum[fn] / cnt[fn]; }
            }
        }

        static Dictionary<string, string> Data(EventRecord e)
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            try
            {
                foreach (Match m in Regex.Matches(e.ToXml(), "<Data Name='([^']+)'>([^<]*)</Data>")) d[m.Groups[1].Value] = m.Groups[2].Value;
            }
            catch { }
            return d;
        }

        static float Ms(Dictionary<string, string> d, string k)
        {
            string v; float f;
            return d.TryGetValue(k, out v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f) ? f : 0;
        }
    }
}
