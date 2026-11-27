using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Tunex
{
    // ---------------------------------------------------------------- «Разбор лагов»: что делал ПК в последние 2 минуты
    class ProcLoad
    {
        public string Name;              // имя exe без расширения, маленькими буквами
        public float Cpu, DiskMb, Faults, MemGb;
    }

    class LagFrame
    {
        public DateTime T;
        public float Cpu = -1, Gpu = -1, Ram = -1, CpuTemp = -1, GpuTemp = -1, AvailGb = -1;
        public float Disk = -1;          // самый занятый диск, % времени
        public string DiskName;          // его буквы, например «D:»
        public float DiskMb, Faults;     // чтение и запись всех программ, МБ/с; жёсткие ошибки страниц в секунду
        public List<ProcLoad> Top = new List<ProcLoad>();
        public List<string> Started = new List<string>();
    }

    class LagCause
    {
        public string Title, Text, Fix, Action;   // Action — раздел Tunex, где это исправить
        public int Score, At = -1;                // At — секунда в записи, где было сильнее всего
    }

    class LagMoment
    {
        public string Id, Time, Game;
        public bool Demo;
        public List<LagCause> Causes = new List<LagCause>();
        public float[] Cpu, Disk, Ram, Gpu;       // по секундам, от старых к новым; -1 — нет данных
        [ScriptIgnore] public DateTime When { get { return DateTime.Parse(Time, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); } }
        [ScriptIgnore] public LagCause Main { get { return Causes.Count > 0 ? Causes[0] : null; } }
        [ScriptIgnore] public bool Calm { get { return Causes.Count == 0 || Causes[0].Score <= 0; } }
    }

    // Запись: раз в секунду, одним вызовом Windows — все процессы сразу (без открытия каждого), плюс занятость дисков
    static class LagRecorder
    {
        public const int Seconds = 120;
        static readonly List<LagFrame> frames = new List<LagFrame>();
        static readonly object sync = new object();
        static Timer timer;
        static int busy;

        class Prev { public long Cpu, Io; public uint Faults; public long Created; }
        static Dictionary<long, Prev> prev;
        static DateTime prevT;
        static readonly List<KeyValuePair<string, PerformanceCounter>> disks = new List<KeyValuePair<string, PerformanceCounter>>();
        static bool disksReady;

        public static void Start() { if (timer == null) timer = new Timer(delegate { Tick(); }, null, 1500, 1000); }
        public static void Stop() { if (timer != null) { timer.Dispose(); timer = null; } }

        public static List<LagFrame> Frames { get { lock (sync) return new List<LagFrame>(frames); } }

        static void Tick()
        {
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            try
            {
                LagFrame f = Sample();
                if (f == null) return;
                lock (sync) { frames.Add(f); while (frames.Count > Seconds) frames.RemoveAt(0); }
            }
            catch { }
            finally { Interlocked.Exchange(ref busy, 0); }
        }

        static LagFrame Sample()
        {
            DateTime now = DateTime.Now;
            List<Proc> list = Snapshot();
            if (list == null) return null;
            LagFrame f = new LagFrame(); f.T = now;
            Sample s = App.Manager != null && App.Manager.Sensors != null ? App.Manager.Sensors.Latest : null;
            if (s != null && (now - s.T).TotalSeconds < 3) { f.Cpu = s.Cpu; f.Gpu = s.Gpu; f.Ram = s.Ram; f.CpuTemp = s.CpuTemp; f.GpuTemp = s.GpuTemp; }
            Native.MEMORYSTATUSEX m = new Native.MEMORYSTATUSEX();
            if (Native.GlobalMemoryStatusEx(m)) { f.AvailGb = m.ullAvailPhys / 1073741824f; if (f.Ram < 0) f.Ram = (m.ullTotalPhys - m.ullAvailPhys) * 100f / m.ullTotalPhys; }
            ReadDisks(f);

            Dictionary<long, Prev> cur = new Dictionary<long, Prev>();
            Dictionary<string, ProcLoad> by = new Dictionary<string, ProcLoad>();
            double span = prev != null ? (now - prevT).TotalSeconds : 0;
            double cpuDen = span * 1e7 * Environment.ProcessorCount / 100.0;
            foreach (Proc p in list)
            {
                long key = ((long)p.Pid << 32) ^ (p.Created & 0xFFFFFFFF);
                Prev pv = new Prev(); pv.Cpu = p.Cpu; pv.Io = p.Io; pv.Faults = p.Faults; pv.Created = p.Created;
                cur[key] = pv;
                if (prev == null || span <= 0.2) continue;
                Prev o;
                bool fresh = !prev.TryGetValue(key, out o);
                if (fresh && p.Created > prevT.AddSeconds(-1).ToFileTime()) f.Started.Add(p.Name);
                ProcLoad l;
                if (!by.TryGetValue(p.Name, out l)) { l = new ProcLoad(); l.Name = p.Name; by[p.Name] = l; }
                l.MemGb += p.Ws / 1073741824f;
                if (fresh) continue;
                l.Cpu += (float)(Math.Max(0, p.Cpu - o.Cpu) / cpuDen);
                l.DiskMb += (float)(Math.Max(0, p.Io - o.Io) / 1048576.0 / span);
                l.Faults += (float)(Math.Max(0, (long)p.Faults - o.Faults) / span);
            }
            bool first = prev == null;
            prev = cur; prevT = now;
            if (first) return null;

            List<ProcLoad> all = new List<ProcLoad>(by.Values);
            foreach (ProcLoad l in all) { f.DiskMb += l.DiskMb; f.Faults += l.Faults; }
            // Хранить немного: самые активные по процессору, диску и памяти
            AddTop(f.Top, all, delegate(ProcLoad l) { return l.Cpu; }, 5, 1f);
            AddTop(f.Top, all, delegate(ProcLoad l) { return l.DiskMb; }, 4, 0.5f);
            AddTop(f.Top, all, delegate(ProcLoad l) { return l.Faults; }, 2, 50f);
            AddTop(f.Top, all, delegate(ProcLoad l) { return l.MemGb; }, 3, 0.3f);
            return f;
        }

        static void AddTop(List<ProcLoad> top, List<ProcLoad> all, Func<ProcLoad, float> by, int n, float min)
        {
            all.Sort(delegate(ProcLoad a, ProcLoad b) { return by(b).CompareTo(by(a)); });
            for (int i = 0; i < all.Count && i < n; i++)
            {
                if (by(all[i]) < min) break;
                if (!top.Contains(all[i])) top.Add(all[i]);
            }
        }

        static void ReadDisks(LagFrame f)
        {
            if (!disksReady)
            {
                disksReady = true;
                try
                {
                    foreach (string inst in new PerformanceCounterCategory("PhysicalDisk").GetInstanceNames())
                    {
                        if (inst == "_Total") continue;
                        try
                        {
                            PerformanceCounter pc = new PerformanceCounter("PhysicalDisk", "% Idle Time", inst); pc.NextValue();
                            // «0 C: D:» → «C: D:»
                            int sp = inst.IndexOf(' ');
                            disks.Add(new KeyValuePair<string, PerformanceCounter>(sp > 0 ? inst.Substring(sp + 1).Trim() : inst, pc));
                        }
                        catch { }
                    }
                }
                catch { }
            }
            foreach (KeyValuePair<string, PerformanceCounter> d in disks)
            {
                try
                {
                    float busy = Math.Max(0, Math.Min(100, 100 - d.Value.NextValue()));
                    if (busy > f.Disk) { f.Disk = busy; f.DiskName = d.Key; }
                }
                catch { }
            }
        }

        // ---- NtQuerySystemInformation(SystemProcessInformation)
        class Proc { public int Pid; public string Name; public long Cpu, Io, Ws, Created; public uint Faults; }

        [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int cls, IntPtr buf, int len, out int ret);

        static int bufLen = 1 << 20;

        static List<Proc> Snapshot()
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                IntPtr buf = Marshal.AllocHGlobal(bufLen);
                try
                {
                    int ret;
                    int st = NtQuerySystemInformation(5, buf, bufLen, out ret);
                    if (st == unchecked((int)0xC0000004)) { bufLen = Math.Max(bufLen * 2, ret + 65536); continue; }   // мало места
                    if (st != 0) return null;
                    return Parse(buf);
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            return null;
        }

        static List<Proc> Parse(IntPtr buf)
        {
            List<Proc> l = new List<Proc>();
            bool x64 = IntPtr.Size == 8;
            // Смещения полей SYSTEM_PROCESS_INFORMATION для 64 и 32 бит
            int oName = 56, oPid = x64 ? 80 : 68, oRead = x64 ? 232 : 160;
            long pos = 0;
            for (int guard = 0; guard < 4096; guard++)
            {
                IntPtr p = new IntPtr(buf.ToInt64() + pos);
                int next = Marshal.ReadInt32(p, 0);
                Proc x = new Proc();
                x.Faults = (uint)Marshal.ReadInt32(p, 16);
                x.Created = Marshal.ReadInt64(p, 32);
                x.Cpu = Marshal.ReadInt64(p, 40) + Marshal.ReadInt64(p, 48);
                int nameLen = Marshal.ReadInt16(p, oName) & 0xFFFF;
                IntPtr namePtr = Marshal.ReadIntPtr(p, oName + (x64 ? 8 : 4));
                x.Pid = (int)Marshal.ReadIntPtr(p, oPid).ToInt64();
                x.Ws = Marshal.ReadInt64(p, 8);   // личная память (без общих страниц), как в Диспетчере задач
                x.Io = Marshal.ReadInt64(p, oRead) + Marshal.ReadInt64(p, oRead + 8);
                if (x.Pid != 0 && nameLen > 0 && namePtr != IntPtr.Zero)
                {
                    string n = Marshal.PtrToStringUni(namePtr, nameLen / 2).ToLowerInvariant();
                    if (n.EndsWith(".exe")) n = n.Substring(0, n.Length - 4);
                    x.Name = n;
                    l.Add(x);
                }
                if (next == 0) break;
                pos += (uint)next;
            }
            return l;
        }

        // Последний кадр — для самопроверки
        public static int Count { get { lock (sync) return frames.Count; } }
        public static void TickNow() { Tick(); }
    }

    // ---------------------------------------------------------------- Разбор: что из записанного похоже на причину лага
    static class LagAnalyzer
    {
        public const int Window = 40;   // лаг был незадолго до нажатия: смотрим последние 40 секунд

        // имя процесса → { понятное название, вид }
        static readonly string[][] Known = {
            new[] { "msmpeng", "Защитник Windows", "defender" }, new[] { "mpcmdrun", "Защитник Windows", "defender" },
            new[] { "mpdefendercoreservice", "Защитник Windows", "defender" }, new[] { "nissrv", "Защитник Windows", "defender" },
            new[] { "tiworker", "Обновления Windows", "updates" }, new[] { "trustedinstaller", "Обновления Windows", "updates" },
            new[] { "mousocoreworker", "Обновления Windows", "updates" }, new[] { "usoclient", "Обновления Windows", "updates" },
            new[] { "wuauclt", "Обновления Windows", "updates" }, new[] { "sihclient", "Обновления Windows", "updates" },
            new[] { "dismhost", "Обновления Windows", "updates" }, new[] { "setuphost", "Обновления Windows", "updates" },
            new[] { "searchindexer", "Поиск Windows", "indexer" }, new[] { "searchprotocolhost", "Поиск Windows", "indexer" },
            new[] { "searchfilterhost", "Поиск Windows", "indexer" },
            new[] { "compattelrunner", "Сбор данных совместимости Windows", "telemetry" }, new[] { "devicecensus", "Сбор данных Windows", "telemetry" },
            new[] { "steam", "Steam", "launcher" }, new[] { "steamwebhelper", "Steam", "launcher" }, new[] { "steamservice", "Steam", "launcher" },
            new[] { "epicgameslauncher", "Epic Games", "launcher" }, new[] { "epicwebhelper", "Epic Games", "launcher" },
            new[] { "battle.net", "Battle.net", "launcher" }, new[] { "upc", "Ubisoft Connect", "launcher" }, new[] { "uplaywebcore", "Ubisoft Connect", "launcher" },
            new[] { "eadesktop", "EA app", "launcher" }, new[] { "eabackgroundservice", "EA app", "launcher" },
            new[] { "onedrive", "OneDrive", "cloud" }, new[] { "dropbox", "Dropbox", "cloud" }, new[] { "googledrivefs", "Google Диск", "cloud" },
            new[] { "yandexdisk2", "Яндекс Диск", "cloud" }, new[] { "yandexdisk", "Яндекс Диск", "cloud" },
            new[] { "chrome", "Google Chrome", "browser" }, new[] { "msedge", "Microsoft Edge", "browser" }, new[] { "firefox", "Firefox", "browser" },
            new[] { "opera", "Opera", "browser" }, new[] { "browser", "Яндекс Браузер", "browser" }, new[] { "brave", "Brave", "browser" },
            new[] { "qbittorrent", "qBittorrent", "torrent" }, new[] { "utorrent", "uTorrent", "torrent" }, new[] { "bittorrent", "BitTorrent", "torrent" },
            new[] { "discord", "Discord", "chat" }, new[] { "telegram", "Telegram", "chat" },
            new[] { "googleupdate", "Обновление Google", "updater" }, new[] { "microsoftedgeupdate", "Обновление Edge", "updater" },
            new[] { "adobearm", "Обновление Adobe", "updater" }, new[] { "officeclicktorun", "Microsoft Office", "updater" },
            new[] { "obs64", "OBS", "rec" },
            new[] { "system", "Система Windows (драйверы)", "system" }, new[] { "memory compression", "Сжатие памяти Windows", "memory" },
            new[] { "svchost", "Службы Windows", "system" }, new[] { "dwm", "Оформление окон Windows", "system" }, new[] { "audiodg", "Звук Windows", "system" },
            new[] { "nvcontainer", "NVIDIA", "nvidia" }, new[] { "nvdisplay.container", "NVIDIA", "nvidia" }, new[] { "nvidia share", "NVIDIA", "nvidia" },
        };
        static readonly string[] Ignore = { "tunex", "idle", "registry", "presentmon-2.6.0", "presentmon" };

        static string[] Find(string name) { foreach (string[] k in Known) if (k[0] == name) return k; return null; }
        public static string KindOf(string name) { string[] k = Find(name); return k != null ? k[2] : ""; }

        public static string NameOf(string name)
        {
            string[] k = Find(name);
            if (k != null) return k[1];
            try
            {
                Process[] ps = Process.GetProcessesByName(name);
                try
                {
                    foreach (Process p in ps)
                    {
                        string d = FileVersionInfo.GetVersionInfo(p.MainModule.FileName).FileDescription;
                        if (!string.IsNullOrEmpty(d) && d.Trim().Length > 1) return d.Trim();
                    }
                }
                finally { foreach (Process p in ps) p.Dispose(); }
            }
            catch { }
            return name.Length > 0 ? char.ToUpperInvariant(name[0]) + name.Substring(1) : name;
        }

        public static LagMoment Analyze(List<LagFrame> all, string game)
        {
            LagMoment m = new LagMoment();
            m.Id = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            m.Time = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            m.Game = game;
            int n = all.Count;
            m.Cpu = new float[n]; m.Disk = new float[n]; m.Ram = new float[n]; m.Gpu = new float[n];
            for (int i = 0; i < n; i++) { m.Cpu[i] = all[i].Cpu; m.Disk[i] = all[i].Disk; m.Ram[i] = all[i].Ram; m.Gpu[i] = all[i].Gpu; }
            if (n == 0) return m;

            string g = game != null ? game.ToLowerInvariant() : null;
            int from = Math.Max(0, n - Window);
            List<LagCause> causes = new List<LagCause>();
            HashSet<string> named = new HashSet<string>();

            // 1. Диск занят почти полностью
            {
                List<int> hot = new List<int>(); float max = 0; string dn = null;
                for (int i = from; i < n; i++) if (all[i].Disk >= 85) { hot.Add(i); if (all[i].Disk > max) { max = all[i].Disk; dn = all[i].DiskName; } }
                if (hot.Count >= 2)
                {
                    string who = Culprit(all, hot, delegate(ProcLoad l) { return l.DiskMb; }, null, 1f);
                    LagCause c = new LagCause(); c.Score = 50 + Math.Min(30, hot.Count * 3); c.At = Peak(all, hot, delegate(LagFrame f) { return f.Disk; });
                    c.Title = "Диск" + (string.IsNullOrEmpty(dn) ? "" : " " + dn) + " был занят на " + App.Pct(max);
                    DiskText(c, who, g, hot.Count);
                    if (who != null) named.Add(who);
                    causes.Add(c);
                }
            }

            // 2. Кончилась оперативная память
            {
                List<int> hot = new List<int>(); float minAvail = float.MaxValue; float maxRam = 0;
                for (int i = from; i < n; i++)
                {
                    LagFrame f = all[i];
                    bool low = (f.AvailGb >= 0 && f.AvailGb < 0.7f) || f.Ram >= 93 || (f.Faults >= 400 && f.Ram >= 85);
                    if (low) { hot.Add(i); if (f.AvailGb >= 0) minAvail = Math.Min(minAvail, f.AvailGb); maxRam = Math.Max(maxRam, f.Ram); }
                }
                if (hot.Count >= 2)
                {
                    LagCause c = new LagCause(); c.Score = 55 + Math.Min(25, hot.Count * 2); c.At = hot[hot.Count / 2];
                    c.Title = "Закончилась оперативная память";
                    List<string> hogs = BigMemory(all[c.At], g);
                    c.Text = (minAvail < float.MaxValue ? "Свободно оставалось всего " + Gb(minAvail) : "Память была занята на " + App.Pct(maxRam)) +
                        ". Windows начала перекладывать часть памяти на диск — отсюда фризы." + (hogs.Count > 0 ? " Больше всего памяти занимали: " + string.Join(", ", hogs.ToArray()) + "." : "");
                    c.Fix = "Закройте браузер и другие тяжёлые программы перед игрой — это делает «Перед игрой» одной кнопкой или сам при запуске игры.";
                    c.Action = "prep";
                    causes.Add(c);
                }
            }

            // 3. Процессор: занят полностью или посторонняя программа резко проснулась
            {
                List<int> hot = new List<int>();
                for (int i = from; i < n; i++) if (all[i].Cpu >= 90) hot.Add(i);
                string who = null; float share = 0; List<int> whoHot = new List<int>();
                // самая прожорливая посторонняя программа за окно
                Dictionary<string, float> sum = new Dictionary<string, float>(); Dictionary<string, int> secs = new Dictionary<string, int>();
                for (int i = from; i < n; i++)
                    foreach (ProcLoad l in all[i].Top)
                    {
                        if (l.Name == g || Array.IndexOf(Ignore, l.Name) >= 0) continue;
                        if (l.Cpu >= 20) { int s; secs.TryGetValue(l.Name, out s); secs[l.Name] = s + 1; }
                        float v; sum.TryGetValue(l.Name, out v); sum[l.Name] = v + l.Cpu;
                    }
                foreach (KeyValuePair<string, int> kv in secs)
                    if (kv.Value >= 2 && (who == null || sum[kv.Key] > sum[who])) who = kv.Key;
                if (who != null)
                {
                    float peak = 0;
                    for (int i = from; i < n; i++) foreach (ProcLoad l in all[i].Top) if (l.Name == who && l.Cpu >= 20) { whoHot.Add(i); peak = Math.Max(peak, l.Cpu); }
                    share = peak;
                }
                if (who != null && !named.Contains(who))
                {
                    LagCause c = new LagCause(); c.Score = (hot.Count >= 2 ? 50 : 32) + Math.Min(20, whoHot.Count * 2);
                    c.At = PeakProc(all, whoHot, who);
                    string nm = NameOf(who);
                    c.Title = nm + " занимал процессор на " + App.Pct(share);
                    CpuText(c, who, nm);
                    named.Add(who);
                    causes.Add(c);
                }
                else if (hot.Count >= 3)
                {
                    LagCause c = new LagCause(); c.Score = 40 + Math.Min(20, hot.Count); c.At = Peak(all, hot, delegate(LagFrame f) { return f.Cpu; });
                    c.Title = "Процессор работал на пределе";
                    c.Text = (g != null ? "Почти всю нагрузку давала сама игра." : "Процессор был занят почти полностью.") + " В тяжёлых сценах он не успевает готовить кадры, и игра подтормаживает.";
                    c.Fix = "Понизьте настройки, которые нагружают процессор: дальность прорисовки, количество объектов и людей, физику. Ограничьте FPS чуть ниже среднего — кадры пойдут ровнее.";
                    causes.Add(c);
                }
            }

            // 4. Перегрев
            {
                float ct = -1, gt = -1; int ci = -1, gi = -1;
                for (int i = from; i < n; i++) { if (all[i].CpuTemp > ct) { ct = all[i].CpuTemp; ci = i; } if (all[i].GpuTemp > gt) { gt = all[i].GpuTemp; gi = i; } }
                if (ct >= 95 || gt >= 88)
                {
                    bool cpu = ct >= 95;
                    LagCause c = new LagCause(); c.Score = 45; c.At = cpu ? ci : gi;
                    c.Title = cpu ? "Процессор перегрелся до " + App.Deg(ct) : "Видеокарта перегрелась до " + App.Deg(gt);
                    c.Text = "При такой температуре частоты сбрасываются, чтобы не сгореть, — FPS резко падает.";
                    c.Fix = "Почистите компьютер от пыли и проверьте, что вентиляторы крутятся. У ноутбука — не ставьте его на мягкое, нужна подставка.";
                    causes.Add(c);
                }
            }

            // 5. Внезапно запустилась фоновая программа
            {
                for (int i = n - 1; i >= from; i--)
                {
                    foreach (string s in all[i].Started)
                    {
                        string kind = KindOf(s);
                        if (kind != "updates" && kind != "defender" && kind != "telemetry" && kind != "updater" && kind != "indexer") continue;
                        string nm = NameOf(s);
                        bool dup = false; foreach (string x in named) if (NameOf(x) == nm) dup = true;
                        if (dup) continue;
                        LagCause c = new LagCause(); c.Score = 25; c.At = i;
                        c.Title = "В этот момент запустилась программа: " + nm;
                        c.Text = "Фоновые задачи Windows и программ просыпаются сами и могут на несколько секунд занять диск и процессор.";
                        c.Fix = kind == "updates" ? "Включите «Не беспокоить» в «Перед игрой» — обновления Windows подождут до конца игры." : "Обычно это проходит само. Если повторяется в каждой игре — отключите эту программу в автозагрузке.";
                        if (kind == "updates") c.Action = "prep";
                        named.Add(s);
                        causes.Add(c);
                    }
                }
            }

            // 6. Видеокарта на пределе — не сбой, но объясняет низкий FPS
            {
                List<int> hot = new List<int>();
                for (int i = from; i < n; i++) if (all[i].Gpu >= 97) hot.Add(i);
                if (hot.Count >= 5)
                {
                    LagCause c = new LagCause(); c.Score = 20; c.At = hot[hot.Count / 2];
                    c.Title = "Видеокарта работала на пределе";
                    c.Text = "Это не сбой: видеокарта выдавала всё, что может. В тяжёлых сценах FPS проседает.";
                    c.Fix = "Снизьте качество теней, сглаживания или разрешение, либо включите DLSS или FSR, если игра их поддерживает.";
                    causes.Add(c);
                }
            }

            causes.Sort(delegate(LagCause a, LagCause b) { return b.Score.CompareTo(a.Score); });
            if (causes.Count == 0)
            {
                LagCause c = new LagCause(); c.Score = 0;
                c.Title = "Компьютер в этот момент был спокоен";
                c.Text = "Ни диск, ни процессор, ни память не были перегружены, перегрева не было, посторонние программы не просыпались. Скорее всего, лаг был сетевой или внутри самой игры.";
                c.Fix = "Проверьте пинг в «Перед игрой» — Tunex покажет, виноват ли Wi-Fi, провайдер или далёкий сервер.";
                c.Action = "prep";
                causes.Add(c);
            }
            m.Causes = causes;
            return m;
        }

        static void DiskText(LagCause c, string who, string game, int secs)
        {
            string kind = who != null ? KindOf(who) : "";
            string nm = who != null ? NameOf(who) : null;
            string len = secs >= 5 ? " несколько секунд подряд" : "";
            if (who != null && who == game)
            {
                c.Text = "Игра сама подгружала данные с диска" + len + ", и он не успевал.";
                c.Fix = "Если игра стоит на обычном жёстком диске (HDD), перенесите её на SSD — подгрузки станут почти незаметны. Где лежат игры, видно в «Оценке ПК».";
                c.Action = "pc";
                return;
            }
            switch (kind)
            {
                case "defender":
                    c.Text = "Защитник Windows проверял файлы" + len + " — игре не хватало диска.";
                    c.Fix = "Если это повторяется, добавьте папку игры в исключения: «Безопасность Windows» → «Защита от вирусов и угроз» → «Исключения». Добавляйте только игры из Steam и других магазинов.";
                    break;
                case "updates":
                    c.Text = "Windows скачивала или ставила обновления" + len + ".";
                    c.Fix = "Включите «Не беспокоить» в «Перед игрой» — обновления подождут до конца игры.";
                    c.Action = "prep"; break;
                case "indexer":
                    c.Text = "Поиск Windows перебирал файлы на диске" + len + ".";
                    c.Fix = "Обычно это проходит само. Если часто — уберите папку с играми из индексирования: «Параметры» → «Поиск» → «Поиск в Windows».";
                    break;
                case "launcher":
                    c.Text = nm + " скачивал или проверял обновления игр" + len + ".";
                    c.Fix = "В настройках " + nm + " запретите загрузки во время игры.";
                    break;
                case "cloud":
                    c.Text = nm + " синхронизировал файлы" + len + ".";
                    c.Fix = "Приостановите синхронизацию на время игры или закрывайте " + nm + " в «Перед игрой».";
                    c.Action = "prep"; break;
                case "memory":
                    c.Text = "Windows перекладывала память на диск: оперативной памяти не хватало.";
                    c.Fix = "Закройте браузер и другие тяжёлые программы перед игрой — это делает «Перед игрой».";
                    c.Action = "prep"; break;
                default:
                    if (nm != null)
                    {
                        c.Text = nm + " активно читал и писал на диск" + len + " — игре не хватало диска.";
                        c.Fix = kind == "browser" || kind == "torrent" || kind == "chat" ? "Закрывайте " + nm + " на время игры — это делает «Перед игрой»." : "Если это повторяется, закрывайте " + nm + " перед игрой.";
                        if (kind == "browser" || kind == "torrent" || kind == "chat") c.Action = "prep";
                    }
                    else
                    {
                        c.Text = "Диск был занят" + len + ", но ни одна программа не выделялась — похоже, работала сама Windows.";
                        c.Fix = "Если это повторяется, проверьте диск в «Оценке ПК»: старый HDD или почти заполненный диск часто дают такие фризы.";
                        c.Action = "pc";
                    }
                    break;
            }
        }

        static void CpuText(LagCause c, string who, string nm)
        {
            switch (KindOf(who))
            {
                case "defender":
                    c.Text = "Защитник Windows проверял файлы и отнимал процессор у игры.";
                    c.Fix = "Если это повторяется, добавьте папку игры в исключения: «Безопасность Windows» → «Защита от вирусов и угроз» → «Исключения».";
                    break;
                case "updates":
                    c.Text = "Windows готовила обновления — это тяжёлая работа для процессора.";
                    c.Fix = "Включите «Не беспокоить» в «Перед игрой» — обновления подождут до конца игры."; c.Action = "prep"; break;
                case "indexer": case "telemetry":
                    c.Text = "Фоновая задача Windows проснулась во время игры.";
                    c.Fix = "Обычно это проходит само. Если повторяется в каждой игре — напишите название этой задачи в поиск, её можно отключить."; break;
                case "browser":
                    c.Text = nm + " в фоне занимал процессор — например, видео или тяжёлая вкладка.";
                    c.Fix = "Закрывайте браузер на время игры — это делает «Перед игрой» одной кнопкой."; c.Action = "prep"; break;
                case "launcher":
                    c.Text = nm + " распаковывал или проверял обновления игр.";
                    c.Fix = "В настройках " + nm + " запретите загрузки во время игры."; break;
                case "rec":
                    c.Text = "Запись или трансляция занимает процессор.";
                    c.Fix = "Переключите кодировщик в OBS на видеокарту (NVENC, AMF или QuickSync)."; break;
                case "system":
                    c.Text = nm + " занимали процессор. Часто так бывает из-за драйвера или службы Windows.";
                    c.Fix = "Обновите драйверы видеокарты и сетевой карты. Если повторяется — проверьте ПК в «Оценке ПК»."; c.Action = "pc"; break;
                default:
                    c.Text = nm + " в фоне отнимал процессор у игры.";
                    c.Fix = "Закрывайте " + nm + " на время игры — это делает «Перед игрой»."; c.Action = "prep"; break;
            }
        }

        // Кто сильнее всех грузил в отмеченные секунды
        static string Culprit(List<LagFrame> all, List<int> hot, Func<ProcLoad, float> by, string skip, float min)
        {
            Dictionary<string, float> sum = new Dictionary<string, float>();
            foreach (int i in hot)
                foreach (ProcLoad l in all[i].Top)
                {
                    if (l.Name == skip || Array.IndexOf(Ignore, l.Name) >= 0) continue;
                    float v; sum.TryGetValue(l.Name, out v); sum[l.Name] = v + by(l);
                }
            string best = null; float bv = 0, total = 0;
            foreach (KeyValuePair<string, float> kv in sum) { total += kv.Value; if (kv.Value > bv) { bv = kv.Value; best = kv.Key; } }
            return best != null && bv >= min * hot.Count && bv >= total * 0.3f ? best : null;
        }

        static int Peak(List<LagFrame> all, List<int> hot, Func<LagFrame, float> by)
        {
            int b = hot[0]; foreach (int i in hot) if (by(all[i]) > by(all[b])) b = i; return b;
        }

        static int PeakProc(List<LagFrame> all, List<int> hot, string who)
        {
            int b = -1; float bv = -1;
            foreach (int i in hot) foreach (ProcLoad l in all[i].Top) if (l.Name == who && l.Cpu > bv) { bv = l.Cpu; b = i; }
            return b;
        }

        static List<string> BigMemory(LagFrame f, string game)
        {
            List<ProcLoad> l = new List<ProcLoad>(f.Top);
            l.Sort(delegate(ProcLoad a, ProcLoad b) { return b.MemGb.CompareTo(a.MemGb); });
            List<string> r = new List<string>();
            foreach (ProcLoad p in l)
            {
                if (p.MemGb < 0.3f || Array.IndexOf(Ignore, p.Name) >= 0) continue;
                string nm = p.Name == game ? "игра" : NameOf(p.Name);
                if (!r.Contains(nm + " (" + Gb(p.MemGb) + ")")) r.Add(nm + " (" + Gb(p.MemGb) + ")");
                if (r.Count >= 3) break;
            }
            return r;
        }

        public static string Gb(float v) { return v < 1 ? Math.Round(v * 1024 / 10) * 10 + " МБ" : v.ToString("0.#", CultureInfo.GetCultureInfo("ru-RU")) + " ГБ"; }
    }

    // ---------------------------------------------------------------- Сохранённые разборы: %APPDATA%\Tunex\lags.json
    static class LagHistory
    {
        public const int Keep = 30;
        static List<LagMoment> list;
        public static event Action<LagMoment> Added;

        static string FilePath { get { return Path.Combine(AppState.Dir, "lags.json"); } }
        static JavaScriptSerializer Json() { JavaScriptSerializer j = new JavaScriptSerializer(); j.MaxJsonLength = int.MaxValue; return j; }

        public static List<LagMoment> All
        {
            get
            {
                if (list == null)
                {
                    try { if (File.Exists(FilePath)) list = Json().Deserialize<List<LagMoment>>(File.ReadAllText(FilePath, Encoding.UTF8)); } catch { }
                    if (list == null) list = new List<LagMoment>();
                }
                return list;
            }
        }

        public static void Add(LagMoment m)
        {
            List<LagMoment> l = All;
            l.Add(m);
            while (l.Count > Keep) l.RemoveAt(0);
            try
            {
                Directory.CreateDirectory(AppState.Dir);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, Json().Serialize(l), new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null); else File.Move(tmp, FilePath);
            }
            catch { }
            if (Added != null) Added(m);
        }

        public static int Since(DateTime t)
        {
            int n = 0;
            foreach (LagMoment m in All) { try { if (m.When >= t) n++; } catch { } }
            return n;
        }

        // Горячая клавиша или кнопка: разобрать то, что записано, в фоне; done вызывается в потоке окна
        public static void Capture(string game, Action<LagMoment> done)
        {
            List<LagFrame> f = LagRecorder.Frames;
            ThreadPool.QueueUserWorkItem(delegate
            {
                LagMoment m = null;
                try { m = LagAnalyzer.Analyze(f, game); } catch { }
                try
                {
                    App.Form.BeginInvoke(new Action(delegate
                    {
                        if (m != null && f.Count >= 5) Add(m);
                        done(f.Count >= 5 ? m : null);
                    }));
                }
                catch { }
            });
        }
    }
}
