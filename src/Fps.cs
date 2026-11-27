using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Tunex
{
    // ---------------------------------------------------------------- Intel PresentMon: скачивание и проверка файла
    // Замер FPS делает бесплатная программа Intel PresentMon (консольная версия). Tunex скачивает её один раз
    // с официального GitHub и сверяет размер и SHA-256, чтобы файл нельзя было подменить.
    static class PresentMon
    {
        public const string Version = "2.6.0";
        const string Url = "https://github.com/GameTechDev/PresentMon/releases/download/v2.6.0/PresentMon-2.6.0-x64.exe";
        const long Size = 980320;
        const string Sha256 = "B2A706BC6AD475749E3B7E3409263AA1E6906D45BDCF993F6DBC0F660188F1AF";
        public const string SizeText = "1 МБ";

        static bool verified;

        public static string ExePath { get { return Path.Combine(Path.Combine(AppState.Dir, "tools"), "PresentMon-" + Version + ".exe"); } }

        public static bool Supported { get { return Environment.Is64BitOperatingSystem; } }

        public static bool Installed
        {
            get
            {
                if (verified && File.Exists(ExePath)) return true;
                verified = File.Exists(ExePath) && Check(ExePath);
                return verified;
            }
        }

        static bool Check(string path)
        {
            try
            {
                if (new FileInfo(path).Length != Size) return false;
                using (SHA256 sha = SHA256.Create())
                using (FileStream f = File.OpenRead(path))
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (byte b in sha.ComputeHash(f)) sb.Append(b.ToString("X2"));
                    return sb.ToString() == Sha256;
                }
            }
            catch { return false; }
        }

        // ---- PresentMon закрывается вместе с Tunex, даже если Tunex упал (Job Object с KILL_ON_JOB_CLOSE)
        [StructLayout(LayoutKind.Sequential)]
        struct BasicLimit
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct IoCounters { public ulong a, b, c, d, e, f; }
        [StructLayout(LayoutKind.Sequential)]
        struct ExtendedLimit
        {
            public BasicLimit Basic;
            public IoCounters Io;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attr, string name);
        [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int cls, IntPtr info, uint len);
        [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        static IntPtr job;

        public static void BindToTunex(Process p)
        {
            try
            {
                if (job == IntPtr.Zero)
                {
                    IntPtr j = CreateJobObject(IntPtr.Zero, null);
                    ExtendedLimit info = new ExtendedLimit();
                    info.Basic.LimitFlags = 0x2000;   // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                    int len = Marshal.SizeOf(typeof(ExtendedLimit));
                    IntPtr ptr = Marshal.AllocHGlobal(len);
                    try { Marshal.StructureToPtr(info, ptr, false); SetInformationJobObject(j, 9, ptr, (uint)len); }
                    finally { Marshal.FreeHGlobal(ptr); }
                    job = j;
                }
                AssignProcessToJobObject(job, p.Handle);
            }
            catch { }
        }

        // При запуске Tunex: закрыть записи, оставшиеся после аварийного выхода
        public static void CleanupSessions()
        {
            if (!SysInfo.IsAdmin || !File.Exists(ExePath)) return;
            foreach (string s in new string[] { "TunexFps", "TunexGame" })
            {
                try
                {
                    ProcessStartInfo si = new ProcessStartInfo(ExePath, "--terminate_existing_session --session_name " + s);
                    si.UseShellExecute = false; si.CreateNoWindow = true;
                    si.RedirectStandardOutput = true; si.RedirectStandardError = true;
                    using (Process t = Process.Start(si)) t.WaitForExit(4000);
                }
                catch { }
            }
        }

        // null — готово, иначе текст ошибки
        public static string Download()
        {
            if (!Supported) return "нужна 64-битная Windows";
            if (Installed) return null;
            string part = ExePath + ".part";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ExePath));
                try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }   // TLS 1.2
                using (WebClient w = new WebClient())
                {
                    w.Headers[HttpRequestHeader.UserAgent] = "Tunex/" + Theme.Version;
                    w.DownloadFile(Url, part);
                }
                if (!Check(part)) { File.Delete(part); return "файл не прошёл проверку — скачайте ещё раз позже"; }
                if (File.Exists(ExePath)) File.Delete(ExePath);
                File.Move(part, ExePath);
                verified = true;
                return null;
            }
            catch (Exception e)
            {
                try { if (File.Exists(part)) File.Delete(part); } catch { }
                return "не удалось скачать: " + e.Message;
            }
        }
    }

    // ---------------------------------------------------------------- Запись кадров
    // Запускает PresentMon, читает его CSV из вывода и копит время между кадрами для каждого процесса.
    class FpsCapture
    {
        class Proc { public string Name; public readonly List<float> Ms = new List<float>(); }

        readonly object sync = new object();
        readonly Dictionary<int, Proc> procs = new Dictionary<int, Proc>();
        readonly string session;
        Process pm;
        public volatile string Error;
        public DateTime Started;

        public FpsCapture(string sessionName) { session = sessionName; }

        public bool Running { get { try { return pm != null && !pm.HasExited; } catch { return false; } } }

        // processName — только этот exe; null — все процессы
        public string Start(string processName)
        {
            if (!SysInfo.IsAdmin) return "нужны права администратора";
            if (!PresentMon.Installed) return "PresentMon не скачан";
            StringBuilder a = new StringBuilder("--output_stdout --no_console_stats --stop_existing_session --no_track_input --session_name " + session);
            if (processName != null) a.Append(" --process_name \"" + processName.Replace("\"", "") + "\"");
            ProcessStartInfo si = new ProcessStartInfo(PresentMon.ExePath, a.ToString());
            si.UseShellExecute = false; si.CreateNoWindow = true;
            si.RedirectStandardOutput = true; si.RedirectStandardError = true;
            si.StandardOutputEncoding = Encoding.UTF8;
            try { pm = Process.Start(si); }
            catch (Exception e) { return "PresentMon не запустился: " + e.Message; }
            PresentMon.BindToTunex(pm);
            Started = DateTime.Now;
            Process p = pm;
            Thread r = new Thread(delegate() { Read(p); }); r.IsBackground = true; r.Start();
            Thread er = new Thread(delegate()
            {
                try
                {
                    string line;
                    while ((line = p.StandardError.ReadLine()) != null)
                        if (line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0) Error = line.Trim();
                }
                catch { }
            });
            er.IsBackground = true; er.Start();
            return null;
        }

        void Read(Process p)
        {
            try
            {
                string line = p.StandardOutput.ReadLine();
                if (line == null) return;
                string[] head = line.Split(',');
                int iApp = Array.IndexOf(head, "Application"), iPid = Array.IndexOf(head, "ProcessID"), iMs = Array.IndexOf(head, "MsBetweenPresents");
                if (iApp < 0 || iPid < 0 || iMs < 0) { Error = "PresentMon выдал неожиданные данные"; return; }
                int need = Math.Max(iApp, Math.Max(iPid, iMs));
                while ((line = p.StandardOutput.ReadLine()) != null)
                {
                    string[] f = line.Split(',');
                    if (f.Length <= need) continue;
                    int pid; float ms;
                    if (!int.TryParse(f[iPid], out pid)) continue;
                    if (!float.TryParse(f[iMs], NumberStyles.Float, CultureInfo.InvariantCulture, out ms) || ms <= 0 || ms > 5000) continue;
                    lock (sync)
                    {
                        Proc pr;
                        if (!procs.TryGetValue(pid, out pr)) { pr = new Proc(); pr.Name = f[iApp]; procs[pid] = pr; }
                        pr.Ms.Add(ms);
                    }
                }
            }
            catch { }
        }

        // Выкинуть всё, что накопилось (кадры, пока человек переключался в игру)
        public void Reset() { lock (sync) procs.Clear(); Started = DateTime.Now; }

        public void Stop()
        {
            Process p = pm;
            if (p == null) return;
            // Мягкая остановка: так PresentMon закрывает системную сессию записи сам
            try
            {
                ProcessStartInfo si = new ProcessStartInfo(PresentMon.ExePath, "--terminate_existing_session --session_name " + session);
                si.UseShellExecute = false; si.CreateNoWindow = true;
                si.RedirectStandardOutput = true; si.RedirectStandardError = true;
                using (Process t = Process.Start(si)) t.WaitForExit(4000);
            }
            catch { }
            try { if (!p.WaitForExit(4000)) p.Kill(); } catch { }
            Thread.Sleep(100);   // дочитать хвост вывода
        }

        static readonly string[] NotGames = {
            "dwm", "explorer", "tunex", "claude", "msedge", "msedgewebview2", "chrome", "firefox", "opera", "browser", "brave", "yandex",
            "discord", "steam", "steamwebhelper", "epicgameslauncher", "epicwebhelper", "battle.net", "eadesktop", "galaxyclient", "ubisoftconnect",
            "searchhost", "searchapp", "shellexperiencehost", "startmenuexperiencehost", "textinputhost", "applicationframehost", "lockapp",
            "telegram", "code", "spotify", "nvidia overlay", "nvcontainer", "obs64", "obs32", "radeonsoftware", "amdrsserv", "teams", "ms-teams",
            "slack", "zoom", "whatsapp", "notepad", "systemsettings", "taskmgr", "presentmon" };

        public static bool IsNotGame(string exe)
        {
            string n = Path.GetFileNameWithoutExtension(exe ?? "").ToLowerInvariant();
            return Array.IndexOf(NotGames, n) >= 0;
        }

        // Процесс, который считаем игрой: тот, что был на экране, иначе — кто больше всех рисовал, кроме браузеров и т. п.
        public int PickProcess(int preferPid)
        {
            lock (sync)
            {
                Proc pr;
                if (preferPid > 0 && procs.TryGetValue(preferPid, out pr) && pr.Ms.Count >= 20 && !IsNotGame(pr.Name)) return preferPid;
                int best = -1; double bestT = 0;
                foreach (KeyValuePair<int, Proc> kv in procs)
                {
                    if (IsNotGame(kv.Value.Name)) continue;
                    double t = kv.Value.Ms.Count;
                    if (t > bestT) { bestT = t; best = kv.Key; }
                }
                return best;
            }
        }

        public string NameOf(int pid) { lock (sync) { Proc p; return procs.TryGetValue(pid, out p) ? p.Name : null; } }

        public float[] Frames(int pid) { lock (sync) { Proc p; return procs.TryGetValue(pid, out p) ? p.Ms.ToArray() : new float[0]; } }

        // Текущий FPS: кадры за последнюю секунду
        public float LiveFps(int pid)
        {
            lock (sync)
            {
                Proc p;
                if (!procs.TryGetValue(pid, out p) || p.Ms.Count == 0) return -1;
                double sum = 0; int n = 0;
                for (int i = p.Ms.Count - 1; i >= 0 && sum < 1000; i--) { sum += p.Ms[i]; n++; }
                return sum > 0 ? (float)(n * 1000 / sum) : -1;
            }
        }
    }

    // ---------------------------------------------------------------- Итог замера (хранится в state.json)
    class FpsRun
    {
        public string Id, Time, Game;          // Game — имя exe без расширения
        public bool Session;                   // true — записано за всю игру, false — замер по кнопке
        public float Seconds, Avg, Low1, Low01, Max;
        public int Frames, Stutters;           // рывки: кадры, которые шли заметно дольше соседних
        public float CpuTemp = -1, GpuTemp = -1, CpuLoad = -1, GpuLoad = -1;   // максимум температуры и средняя загрузка
        public float[] Graph;                  // FPS по отрезкам времени, для графика

        [System.Web.Script.Serialization.ScriptIgnore]
        public DateTime When { get { return DateTime.Parse(Time, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); } }

        public float StuttersPerMin { get { return Seconds > 0 ? Stutters * 60f / Seconds : 0; } }

        public static FpsRun Compute(string game, float[] ms)
        {
            if (ms == null || ms.Length < 10) return null;
            FpsRun r = new FpsRun();
            r.Id = Guid.NewGuid().ToString("N").Substring(0, 10);
            r.Time = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            r.Game = game;
            double total = 0; foreach (float m in ms) total += m;
            r.Frames = ms.Length;
            r.Seconds = (float)(total / 1000);
            r.Avg = (float)(ms.Length * 1000 / total);
            float[] s = (float[])ms.Clone(); Array.Sort(s);
            r.Low1 = 1000f / Pct(s, 0.99);
            r.Low01 = 1000f / Pct(s, 0.999);
            r.Max = 1000f / Math.Max(0.1f, Pct(s, 0.01));
            float median = Pct(s, 0.5);

            // Рывок — кадр вдвое дольше обычного и дольше 25 мс (заметно глазу); подряд идущие считаем одним рывком
            bool inStutter = false;
            foreach (float m in ms)
            {
                bool st = m > Math.Max(25f, median * 2.5f);
                if (st && !inStutter) r.Stutters++;
                inStutter = st;
            }

            // График: не больше 240 точек
            int points = Math.Max(2, Math.Min(240, (int)Math.Ceiling(r.Seconds)));
            double slice = total / points;
            List<float> g = new List<float>();
            double acc = 0; int n = 0;
            foreach (float m in ms)
            {
                acc += m; n++;
                if (acc >= slice) { g.Add((float)(n * 1000 / acc)); acc = 0; n = 0; }
            }
            if (n > 0 && acc > slice / 3) g.Add((float)(n * 1000 / acc));
            r.Graph = g.ToArray();
            return r;
        }

        static float Pct(float[] sorted, double p)
        {
            int i = (int)Math.Round(p * (sorted.Length - 1));
            return sorted[Math.Max(0, Math.Min(sorted.Length - 1, i))];
        }

        public void AddSensors(Metrics avg, float maxCpuT, float maxGpuT)
        {
            if (avg != null) { CpuLoad = avg.Cpu; GpuLoad = avg.Gpu; }
            CpuTemp = maxCpuT; GpuTemp = maxGpuT;
        }

        // Короткий вывод простыми словами
        public string Verdict(out System.Drawing.Color color)
        {
            float spm = StuttersPerMin;
            if (Avg < 30) { color = Theme.Red; return "Мало кадров — игра будет дёргаться. Понизьте настройки графики или разрешение."; }
            if (spm >= 6 || Low1 < Avg * 0.4f) { color = Theme.Orange; return "Есть заметные рывки. Средний FPS нормальный, но кадры иногда задерживаются."; }
            if (Avg < 55) { color = Theme.Orange; return "Играть можно, но не очень плавно. Для 60 кадров понизьте пару настроек графики."; }
            if (Low1 < Avg * 0.6f || spm >= 2) { color = Theme.Orange; return "В целом плавно, но иногда бывают просадки."; }
            color = Theme.Green; return "Плавно: кадры идут ровно, рывков почти нет.";
        }

        // Советы после игры
        public List<string> Hints()
        {
            List<string> h = new List<string>();
            if (GpuTemp >= 85) h.Add("Видеокарта грелась до " + App.Deg(GpuTemp) + ". Почистите ПК от пыли и проверьте, что вентиляторы крутятся.");
            if (CpuTemp >= 90) h.Add("Процессор грелся до " + App.Deg(CpuTemp) + " — при перегреве он сбрасывает частоту. Проверьте охлаждение.");
            if (StuttersPerMin >= 4) h.Add("Много рывков. Закройте браузер и лишние программы, проверьте, что игра стоит на SSD.");
            if (GpuLoad >= 0 && GpuLoad < 60 && CpuLoad >= 80) h.Add("Видеокарта загружена слабо, а процессор — почти полностью: упор в процессор. Поможет снизить дальность прорисовки и количество объектов.");
            else if (GpuLoad >= 0 && GpuLoad < 50 && Avg < 100) h.Add("Видеокарта загружена слабо. Проверьте, не стоит ли в игре ограничение FPS или вертикальная синхронизация.");
            return h;
        }
    }

    // ---------------------------------------------------------------- Замер по кнопке: отсчёт → запись → итог
    static class FpsMeter
    {
        public enum Phase { Idle, Countdown, Recording }
        public static Phase State = Phase.Idle;
        public static int Duration = 60;          // секунд записи
        public static int Delay = 10;             // секунд, чтобы переключиться в игру (выбирается на странице)
        public static DateTime PhaseEnd;
        public static string Status = "";         // для страницы: что сейчас происходит
        public static FpsRun Last;                // последний результат этого запуска Tunex
        public static event Action<FpsRun, string> Finished;   // результат или текст ошибки

        static FpsCapture cap;
        static int pid = -1;

        // Игра, которую сейчас пишем: окно на экране или тот, кто больше всех рисует
        static int CurrentPid { get { FpsCapture c = cap; return c != null ? c.PickProcess(pid) : -1; } }
        public static string GameName
        {
            get
            {
                FpsCapture c = cap; int p = CurrentPid;
                string n = p > 0 && c != null ? c.NameOf(p) : null;
                return string.IsNullOrEmpty(n) ? null : Path.GetFileNameWithoutExtension(n);
            }
        }
        public static float LiveFps { get { FpsCapture c = cap; int p = CurrentPid; return p > 0 && c != null ? c.LiveFps(p) : -1; } }
        public static float Elapsed { get { return cap != null && State == Phase.Recording ? (float)(DateTime.Now - cap.Started).TotalSeconds : 0; } }

        // delay — секунд до начала записи (0 — сразу)
        public static string Start(int delay)
        {
            if (State != Phase.Idle) return null;
            cap = new FpsCapture("TunexFps");
            string err = cap.Start(null);
            if (err != null) { cap = null; return err; }
            pid = -1;
            if (delay > 0) { State = Phase.Countdown; PhaseEnd = DateTime.Now.AddSeconds(delay); }
            else BeginRecording();
            return null;
        }

        static void BeginRecording()
        {
            cap.Reset();
            State = Phase.Recording;
            PhaseEnd = DateTime.Now.AddSeconds(Duration);
            pid = ForegroundPid();
            Beep(true);
        }

        public static void Cancel()
        {
            if (State == Phase.Idle) return;
            FpsCapture c = cap; cap = null; State = Phase.Idle; pid = -1;
            ThreadPool.QueueUserWorkItem(delegate { c.Stop(); });
        }

        // При закрытии Tunex: остановить запись сразу, чтобы системная сессия PresentMon не осталась висеть
        public static void Shutdown()
        {
            FpsCapture c = cap; cap = null; State = Phase.Idle;
            if (c != null) try { c.Stop(); } catch { }
        }

        // Горячая клавиша: начать сразу или закончить раньше
        public static string Hotkey()
        {
            if (State == Phase.Idle) return Start(0);
            if (State == Phase.Countdown) { BeginRecording(); return null; }
            FinishNow();
            return null;
        }

        public static void FinishNow() { if (State == Phase.Recording) PhaseEnd = DateTime.Now; Tick(); }

        // Раз в секунду из главного окна
        public static void Tick()
        {
            if (State == Phase.Idle) return;
            if (cap != null && cap.Error != null && !cap.Running)
            {
                string e = cap.Error; Cancel();
                if (Finished != null) Finished(null, "PresentMon: " + e);
                return;
            }
            if (DateTime.Now < PhaseEnd)
            {
                // Пока идёт запись, держим в уме процесс на экране — человек мог переключиться в игру позже
                if (State == Phase.Recording && Elapsed < 5) { int f = ForegroundPid(); if (f > 0) pid = f; }
                return;
            }
            if (State == Phase.Countdown) { BeginRecording(); return; }

            FpsCapture c = cap; int prefer = pid;
            DateTime from = c.Started, to = DateTime.Now;
            State = Phase.Idle; cap = null;
            Beep(false);
            ThreadPool.QueueUserWorkItem(delegate
            {
                c.Stop();
                int p = c.PickProcess(prefer);
                FpsRun r = p > 0 ? FpsRun.Compute(Path.GetFileNameWithoutExtension(c.NameOf(p)), c.Frames(p)) : null;
                string err = r == null ? "Не удалось поймать кадры игры. Во время замера игра должна быть на экране." : null;
                if (r != null)
                {
                    SensorService s = App.Manager.Sensors;
                    float ct = -1, gt = -1;
                    foreach (Sample x in s.Recent(Math.Min(SensorService.HistorySeconds, (int)(to - from).TotalSeconds + 2)))
                    {
                        if (x.T < from) continue;
                        ct = Math.Max(ct, x.CpuTemp); gt = Math.Max(gt, x.GpuTemp);
                    }
                    r.AddSensors(s.Average(from, to), ct, gt);
                }
                try
                {
                    App.Form.BeginInvoke(new Action(delegate
                    {
                        if (r != null) { Last = r; FpsHistory.Add(r); }
                        if (Finished != null) Finished(r, err);
                    }));
                }
                catch { }
            });
        }

        static void Beep(bool start)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    if (start) Console.Beep(880, 120);
                    else { Console.Beep(880, 100); Thread.Sleep(60); Console.Beep(1320, 160); }
                }
                catch { }
            });
        }

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);

        public static int ForegroundPid()
        {
            int p; GetWindowThreadProcessId(GetForegroundWindow(), out p);
            return p == Process.GetCurrentProcess().Id ? -1 : p;
        }
    }

    // ---------------------------------------------------------------- Запись за всю игру — для «Итога после игры»
    class FpsSession
    {
        public readonly string Exe;
        readonly FpsCapture cap = new FpsCapture("TunexGame");
        float maxCpuT = -1, maxGpuT = -1;
        double cpuSum, gpuSum; int cpuN, gpuN;

        FpsSession(string exe) { Exe = exe; }

        public static FpsSession TryStart(string exe)
        {
            if (!App.Manager.State.GameSummary || !PresentMon.Installed || !SysInfo.IsAdmin || FpsCapture.IsNotGame(exe)) return null;
            FpsSession s = new FpsSession(exe);
            return s.cap.Start(Path.GetFileName(exe)) == null ? s : null;
        }

        // Раз в несколько секунд, пока игра запущена
        public void Sample()
        {
            Sample x = App.Manager.Sensors.Latest;
            if (x == null) return;
            maxCpuT = Math.Max(maxCpuT, x.CpuTemp); maxGpuT = Math.Max(maxGpuT, x.GpuTemp);
            if (x.Cpu >= 0) { cpuSum += x.Cpu; cpuN++; }
            if (x.Gpu >= 0) { gpuSum += x.Gpu; gpuN++; }
        }

        // Итог; null — игра шла слишком мало или кадров нет
        public FpsRun Finish()
        {
            cap.Stop();
            int p = cap.PickProcess(-1);
            if (p <= 0) return null;
            FpsRun r = FpsRun.Compute(Path.GetFileNameWithoutExtension(Exe), cap.Frames(p));
            if (r == null || r.Seconds < 60) return null;
            r.Session = true;
            Metrics m = new Metrics();
            if (cpuN > 0) m.Cpu = (float)(cpuSum / cpuN);
            if (gpuN > 0) m.Gpu = (float)(gpuSum / gpuN);
            r.AddSensors(m, maxCpuT, maxGpuT);
            return r;
        }
    }

    static class FpsHistory
    {
        public const int Keep = 60;

        public static void Add(FpsRun r)
        {
            List<FpsRun> l = App.Manager.State.FpsRuns;
            l.Add(r);
            while (l.Count > Keep) l.RemoveAt(0);
            App.Manager.State.Save();
        }

        // Предыдущий замер той же игры (того же вида: по кнопке или за всю игру)
        public static FpsRun Previous(FpsRun r)
        {
            List<FpsRun> l = App.Manager.State.FpsRuns;
            int i = l.IndexOf(r);
            if (i < 0) i = l.Count;
            for (int j = i - 1; j >= 0; j--)
                if (string.Equals(l[j].Game, r.Game, StringComparison.OrdinalIgnoreCase) && l[j].Session == r.Session) return l[j];
            return null;
        }

        // Сколько настроек Tunex поменял между двумя замерами
        public static int ChangesBetween(FpsRun a, FpsRun b)
        {
            int n = 0;
            DateTime from = a.When, to = b.When;
            foreach (JournalEntry e in App.Manager.State.Journal)
            {
                if (e.Undone) continue;
                DateTime t; try { t = e.When; } catch { continue; }
                if (t > from && t < to) n += Math.Max(1, e.Items.Count);
            }
            return n;
        }
    }
}
