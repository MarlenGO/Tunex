using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Tunex
{
    // ================================================================ Разгрузка перед игрой
    class AppGroup
    {
        public string Key;          // имя процесса без .exe, в нижнем регистре
        public string Name, Kind, Exe;
        public bool DefaultOn;
        public long Memory; public float Cpu;
        public readonly List<int> Pids = new List<int>();
        public readonly List<IntPtr> Windows = new List<IntPtr>();
    }

    class ClosedApp { public string Key, Name, Exe; }

    static class Unload
    {
        // ключ, название, вид, закрывать по умолчанию
        static readonly string[][] Known = {
            new[] { "chrome", "Google Chrome", "Браузер", "1" }, new[] { "msedge", "Microsoft Edge", "Браузер", "1" },
            new[] { "firefox", "Firefox", "Браузер", "1" }, new[] { "opera", "Opera", "Браузер", "1" },
            new[] { "brave", "Brave", "Браузер", "1" }, new[] { "vivaldi", "Vivaldi", "Браузер", "1" },
            new[] { "browser", "Яндекс Браузер", "Браузер", "1" },
            new[] { "qbittorrent", "qBittorrent", "Торрент", "1" }, new[] { "utorrent", "uTorrent", "Торрент", "1" },
            new[] { "bittorrent", "BitTorrent", "Торрент", "1" }, new[] { "transmission-qt", "Transmission", "Торрент", "1" },
            new[] { "biglybt", "BiglyBT", "Торрент", "1" }, new[] { "tixati", "Tixati", "Торрент", "1" },
            new[] { "onedrive", "OneDrive", "Облако", "1" }, new[] { "dropbox", "Dropbox", "Облако", "1" },
            new[] { "googledrivefs", "Google Диск", "Облако", "1" }, new[] { "yandexdisk2", "Яндекс Диск", "Облако", "1" },
            new[] { "megasync", "MEGA", "Облако", "1" },
            new[] { "creative cloud", "Adobe Creative Cloud", "Фон", "1" }, new[] { "wallpaper32", "Wallpaper Engine", "Фон", "1" },
            new[] { "wallpaper64", "Wallpaper Engine", "Фон", "1" },
            new[] { "epicgameslauncher", "Epic Games", "Лаунчер", "0" }, new[] { "battle.net", "Battle.net", "Лаунчер", "0" },
            new[] { "eadesktop", "EA app", "Лаунчер", "0" }, new[] { "upc", "Ubisoft Connect", "Лаунчер", "0" },
            new[] { "galaxyclient", "GOG Galaxy", "Лаунчер", "0" },
            new[] { "discord", "Discord", "Общение", "0" }, new[] { "telegram", "Telegram", "Общение", "0" },
            new[] { "whatsapp", "WhatsApp", "Общение", "0" }, new[] { "viber", "Viber", "Общение", "0" },
            new[] { "teams", "Microsoft Teams", "Общение", "0" }, new[] { "ms-teams", "Microsoft Teams", "Общение", "0" },
            new[] { "slack", "Slack", "Общение", "0" }, new[] { "zoom", "Zoom", "Общение", "0" }, new[] { "skype", "Skype", "Общение", "0" },
            new[] { "spotify", "Spotify", "Музыка", "0" } };

        // Никогда не предлагать: система, Tunex, Steam и клиенты, без которых не запустится игра, античиты
        static readonly HashSet<string> Never = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "system", "idle", "registry", "memory compression", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost",
            "explorer", "dwm", "sihost", "taskhostw", "ctfmon", "conhost", "fontdrvhost", "audiodg", "spoolsv", "searchhost", "searchui",
            "searchapp", "startmenuexperiencehost", "shellexperiencehost", "textinputhost", "runtimebroker", "applicationframehost",
            "systemsettings", "lockapp", "securityhealthsystray", "msmpeng", "taskmgr", "mmc", "cmd", "powershell", "windowsterminal",
            "tunex", "tunex-setup", "claude", "steam", "steamwebhelper", "steamservice", "riotclientservices", "riotclientux", "riotclientcrashhandler",
            "vgtray", "vgc", "faceit", "faceitclient", "faceitservice", "easyanticheat", "easyanticheat_eos", "beservice", "battleye",
            "nvcontainer", "nvidia share", "nvidia overlay", "nvidia app", "radeonsoftware", "amdrsserv", "gamebar", "gamebarftserver" };

        public const long BigApp = 400L << 20;   // неизвестная программа с окном — показывать, если занимает больше

        // Список программ, которые сейчас занимают ресурсы (около секунды — замер процессора)
        public static List<AppGroup> Scan()
        {
            HashSet<string> games = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string g in App.Manager.State.Games.ToArray()) games.Add(Path.GetFileNameWithoutExtension(g));
            int session = Process.GetCurrentProcess().SessionId;
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            Dictionary<int, List<IntPtr>> windows = VisibleWindows();

            Dictionary<string, AppGroup> map = new Dictionary<string, AppGroup>();
            Dictionary<int, TimeSpan> cpu0 = new Dictionary<int, TimeSpan>();
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (p.SessionId != session) continue;
                    string key = p.ProcessName.ToLowerInvariant();
                    if (Never.Contains(key) || games.Contains(key)) continue;
                    AppGroup g;
                    if (!map.TryGetValue(key, out g))
                    {
                        g = new AppGroup(); g.Key = key; map[key] = g;
                        g.Exe = ImagePath(p.Id);
                    }
                    g.Pids.Add(p.Id);
                    try { g.Memory += p.WorkingSet64; } catch { }
                    try { cpu0[p.Id] = p.TotalProcessorTime; } catch { }
                    List<IntPtr> w;
                    if (windows.TryGetValue(p.Id, out w)) g.Windows.AddRange(w);
                }
                catch { }
                finally { p.Dispose(); }
            }

            // Отбор: известные программы и большие незнакомые с окном (не из папки Windows)
            List<AppGroup> list = new List<AppGroup>();
            foreach (AppGroup g in map.Values)
            {
                string[] k = Find(g.Key);
                if (g.Key == "browser" && (g.Exe == null || g.Exe.IndexOf("yandex", StringComparison.OrdinalIgnoreCase) < 0)) k = null;
                if (k != null) { g.Name = k[1]; g.Kind = k[2]; g.DefaultOn = k[3] == "1"; }
                else
                {
                    if (g.Windows.Count == 0 || g.Memory < BigApp || g.Exe == null) continue;
                    if (g.Exe.StartsWith(win, StringComparison.OrdinalIgnoreCase)) continue;
                    g.Name = Describe(g.Exe, g.Key); g.Kind = "Программа"; g.DefaultOn = false;
                }
                list.Add(g);
            }

            // Процессор: разница времени за 0,8 с
            Stopwatch sw = Stopwatch.StartNew();
            Thread.Sleep(800);
            double ms = sw.Elapsed.TotalMilliseconds * Environment.ProcessorCount;
            foreach (AppGroup g in list)
                foreach (int pid in g.Pids)
                {
                    TimeSpan t0;
                    if (!cpu0.TryGetValue(pid, out t0)) continue;
                    try { using (Process p = Process.GetProcessById(pid)) g.Cpu += (float)((p.TotalProcessorTime - t0).TotalMilliseconds / ms * 100); } catch { }
                }
            list.Sort(delegate(AppGroup a, AppGroup b) { return b.Memory.CompareTo(a.Memory); });
            return list;
        }

        // Одна программа по имени процесса, без отбора — для самопроверки
        public static AppGroup Probe(string key)
        {
            Dictionary<int, List<IntPtr>> windows = VisibleWindows();
            AppGroup g = new AppGroup(); g.Key = key; g.Name = key; g.Kind = "Программа";
            foreach (Process p in Process.GetProcessesByName(key))
            {
                using (p)
                {
                    g.Pids.Add(p.Id);
                    if (g.Exe == null) g.Exe = ImagePath(p.Id);
                    List<IntPtr> w;
                    if (windows.TryGetValue(p.Id, out w)) g.Windows.AddRange(w);
                }
            }
            return g;
        }

        static string[] Find(string key) { foreach (string[] k in Known) if (k[0] == key) return k; return null; }

        static string Describe(string exe, string key)
        {
            try
            {
                string d = FileVersionInfo.GetVersionInfo(exe).FileDescription;
                if (!string.IsNullOrEmpty(d) && d.Trim().Length > 1) return d.Trim();
            }
            catch { }
            return key.Length > 0 ? char.ToUpperInvariant(key[0]) + key.Substring(1) : key;
        }

        // ---- Выбор пользователя: что закрывать
        public static bool Picked(AppGroup g)
        {
            AppState s = App.Manager.State;
            if (s.UnloadPick.Contains(g.Key)) return true;
            if (s.UnloadSkip.Contains(g.Key)) return false;
            return g.DefaultOn;
        }

        public static void SetPicked(AppGroup g, bool on)
        {
            AppState s = App.Manager.State;
            s.UnloadPick.Remove(g.Key); s.UnloadSkip.Remove(g.Key);
            if (on != g.DefaultOn) (on ? s.UnloadPick : s.UnloadSkip).Add(g.Key);
            s.Save();
        }

        // ---- Закрыть: окна — как крестиком; если окон не осталось, а процессы висят — завершить.
        // Если окно осталось (спрашивает о сохранении), программу не трогаем.
        public static List<ClosedApp> Close(List<AppGroup> groups, List<string> failed)
        {
            List<ClosedApp> done = new List<ClosedApp>();
            foreach (AppGroup g in groups)
            {
                if (g.Key == "onedrive" && g.Exe != null) { try { Process.Start(g.Exe, "/shutdown"); } catch { } }
                foreach (IntPtr h in g.Windows) PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero);   // WM_CLOSE
            }
            Stopwatch sw = Stopwatch.StartNew();
            List<AppGroup> left = new List<AppGroup>(groups);
            while (left.Count > 0 && sw.ElapsedMilliseconds < 6000)
            {
                Thread.Sleep(300);
                left.RemoveAll(delegate(AppGroup g) { return Alive(g).Count == 0; });
                if (left.Count > 0 && sw.ElapsedMilliseconds < 2500) continue;
                Dictionary<int, List<IntPtr>> wins = VisibleWindows();
                foreach (AppGroup g in left)
                {
                    bool hasWindow = false;
                    foreach (int pid in Alive(g)) if (wins.ContainsKey(pid)) hasWindow = true;
                    if (!hasWindow) foreach (int pid in Alive(g)) { try { using (Process p = Process.GetProcessById(pid)) p.Kill(); } catch { } }
                }
                if (sw.ElapsedMilliseconds > 4000) break;
            }
            Thread.Sleep(300);
            foreach (AppGroup g in groups)
            {
                if (Alive(g).Count == 0)
                {
                    ClosedApp c = new ClosedApp(); c.Key = g.Key; c.Name = g.Name; c.Exe = g.Exe;
                    if (g.Exe != null) done.Add(c);
                }
                else if (failed != null) failed.Add(g.Name);
            }
            return done;
        }

        static List<int> Alive(AppGroup g)
        {
            List<int> l = new List<int>();
            foreach (int pid in g.Pids)
            {
                try { using (Process p = Process.GetProcessById(pid)) if (!p.HasExited) l.Add(pid); } catch { }
            }
            return l;
        }

        // ---- Вернуть: открыть закрытые программы снова (без прав администратора — через Проводник)
        public static List<string> Restore(List<ClosedApp> apps)
        {
            List<string> names = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ClosedApp c in apps)
            {
                if (string.IsNullOrEmpty(c.Exe) || !File.Exists(c.Exe) || !seen.Add(c.Exe)) continue;
                try
                {
                    if (SysInfo.IsAdmin) Process.Start("explorer.exe", "\"" + c.Exe + "\"");
                    else Process.Start(c.Exe);
                    if (!names.Contains(c.Name)) names.Add(c.Name);
                    Thread.Sleep(400);
                }
                catch { }
            }
            return names;
        }

        public static string Size(long bytes)
        {
            double gb = bytes / 1073741824.0;
            return gb >= 1 ? gb.ToString(gb >= 10 ? "0" : "0.0", CultureInfo.CurrentCulture) + " ГБ" : Math.Max(1, bytes >> 20) + " МБ";
        }

        // ---- Окна
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);

        static Dictionary<int, List<IntPtr>> VisibleWindows()
        {
            Dictionary<int, List<IntPtr>> map = new Dictionary<int, List<IntPtr>>();
            EnumProc cb = delegate(IntPtr h, IntPtr l)
            {
                if (!IsWindowVisible(h) || GetWindow(h, 4) != IntPtr.Zero || GetWindowTextLength(h) == 0) return true;   // 4 — GW_OWNER
                int pid; GetWindowThreadProcessId(h, out pid);
                List<IntPtr> w;
                if (!map.TryGetValue(pid, out w)) map[pid] = w = new List<IntPtr>();
                w.Add(h);
                return true;
            };
            EnumWindows(cb, IntPtr.Zero);
            GC.KeepAlive(cb);
            return map;
        }

        static string ImagePath(int pid)
        {
            IntPtr h = OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION
            if (h == IntPtr.Zero) return null;
            try
            {
                StringBuilder sb = new StringBuilder(1024); int n = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }
    }

    // ================================================================ Пинг до игровых серверов
    class PingTarget
    {
        public string Name, Host;
        public IPAddress Ip;
        public bool Router;
        public int Sent, Lost;
        public readonly List<int> Rtt = new List<int>();
        public float Avg { get { if (Rtt.Count == 0) return -1; float s = 0; foreach (int r in Rtt) s += r; return s / Rtt.Count; } }
        public float Loss { get { return Sent == 0 ? 0 : Lost * 100f / Sent; } }
        // Скачки — среднее изменение пинга между соседними ответами
        public float Jitter
        {
            get
            {
                if (Rtt.Count < 2) return 0;
                float s = 0; for (int i = 1; i < Rtt.Count; i++) s += Math.Abs(Rtt[i] - Rtt[i - 1]);
                return s / (Rtt.Count - 1);
            }
        }
        public bool Ok { get { return Rtt.Count > 0 && Loss < 50; } }
        public bool Unstable { get { return Loss >= 2 || Jitter >= (Router ? 8 : 15); } }
    }

    static class NetPing
    {
        public static readonly string[] Games = { "cs2", "dota2", "valorant" };
        public static string Title(string game) { return game == "cs2" ? "CS2" : game == "dota2" ? "Dota 2" : "Valorant"; }
        static int AppId(string game) { return game == "dota2" ? 570 : 730; }

        // Valorant: у Riot нет открытого списка серверов — меряем до дата-центров Amazon в тех же городах
        static readonly string[][] Aws = {
            new[] { "eu-central-1", "Франкфурт" }, new[] { "eu-north-1", "Стокгольм" }, new[] { "eu-west-2", "Лондон" },
            new[] { "eu-west-3", "Париж" }, new[] { "eu-west-1", "Ирландия" }, new[] { "ap-south-1", "Мумбаи" },
            new[] { "ap-southeast-1", "Сингапур" }, new[] { "ap-northeast-1", "Токио" }, new[] { "ap-northeast-2", "Сеул" },
            new[] { "ap-southeast-2", "Сидней" }, new[] { "us-east-1", "Вирджиния" }, new[] { "us-east-2", "Огайо" },
            new[] { "us-west-1", "Калифорния" }, new[] { "us-west-2", "Орегон" }, new[] { "sa-east-1", "Сан-Паулу" },
            new[] { "ca-central-1", "Монреаль" } };

        // Запасной список Valve, если нет ни интернета до Steam, ни кэша
        static readonly string[][] Fallback = {
            new[] { "Frankfurt", "155.133.226.68" }, new[] { "Stockholm", "162.254.198.41" }, new[] { "Warsaw", "155.133.230.100" },
            new[] { "Vienna", "146.66.155.66" }, new[] { "Amsterdam", "155.133.248.36" }, new[] { "Dubai", "185.25.183.163" },
            new[] { "London", "162.254.196.66" }, new[] { "Madrid", "155.133.246.34" }, new[] { "Mumbai", "155.133.224.20" },
            new[] { "Hong Kong", "103.28.54.163" }, new[] { "Singapore", "103.10.124.116" }, new[] { "Tokyo", "45.121.184.5" },
            new[] { "Sterling", "162.254.192.88" }, new[] { "Los Angeles", "162.254.195.52" }, new[] { "Sao Paulo", "155.133.227.35" } };

        static readonly Dictionary<string, string> Ru = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "Amsterdam", "Амстердам" }, { "Atlanta", "Атланта" }, { "Dallas", "Даллас" }, { "Dubai", "Дубай" }, { "Fujairah", "Фуджейра" },
            { "Buenos Aires", "Буэнос-Айрес" }, { "Frankfurt", "Франкфурт" }, { "Sao Paulo", "Сан-Паулу" }, { "Guam", "Гуам" },
            { "Hong Kong", "Гонконг" }, { "Sterling", "Вашингтон" }, { "Johannesburg", "Йоханнесбург" }, { "Los Angeles", "Лос-Анджелес" },
            { "London", "Лондон" }, { "Lima", "Лима" }, { "Madrid", "Мадрид" }, { "Chicago", "Чикаго" }, { "Paris", "Париж" },
            { "Santiago", "Сантьяго" }, { "Seattle", "Сиэтл" }, { "Seoul", "Сеул" }, { "Singapore", "Сингапур" }, { "Stockholm", "Стокгольм" },
            { "Sydney", "Сидней" }, { "Tokyo", "Токио" }, { "Tokyo Koto City", "Токио" }, { "Vienna", "Вена" }, { "Warsaw", "Варшава" },
            { "Mumbai", "Мумбаи" }, { "Chennai", "Ченнай" }, { "Helsinki", "Хельсинки" }, { "Falkenstein", "Фалькенштайн" },
            { "Chengdu", "Чэнду" }, { "Beijing", "Пекин" }, { "Shanghai", "Шанхай" }, { "Guangzhou", "Гуанчжоу" }, { "Wenatchee", "Уэнатчи" } };

        static string CacheDir { get { return Path.Combine(AppState.Dir, "net"); } }

        // Серверы игры: для CS2 и Dota 2 — список Valve (кэш на 7 дней), для Valorant — Amazon
        public static List<PingTarget> Servers(string game)
        {
            List<PingTarget> list = new List<PingTarget>();
            if (game == "valorant")
            {
                foreach (string[] a in Aws) { PingTarget t = new PingTarget(); t.Name = a[1]; t.Host = "dynamodb." + a[0] + ".amazonaws.com"; list.Add(t); }
                return list;
            }
            string json = ValveConfig(AppId(game));
            Dictionary<string, PingTarget> byCity = new Dictionary<string, PingTarget>();
            if (json != null)
            {
                try
                {
                    Dictionary<string, object> d = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                    Dictionary<string, object> pops = d["pops"] as Dictionary<string, object>;
                    foreach (KeyValuePair<string, object> kv in pops)
                    {
                        Dictionary<string, object> pop = kv.Value as Dictionary<string, object>;
                        object desc, relays;
                        if (pop == null || !pop.TryGetValue("relays", out relays) || !pop.TryGetValue("desc", out desc)) continue;
                        object[] r = relays as object[];
                        if (r == null || r.Length == 0) continue;
                        Dictionary<string, object> r0 = r[0] as Dictionary<string, object>;
                        object ip;
                        if (r0 == null || !r0.TryGetValue("ipv4", out ip)) continue;
                        AddCity(byCity, City(Convert.ToString(desc)), Convert.ToString(ip));
                    }
                }
                catch { byCity.Clear(); }
            }
            if (byCity.Count == 0) foreach (string[] f in Fallback) AddCity(byCity, City(f[0]), f[1]);
            foreach (PingTarget t in byCity.Values) list.Add(t);
            return list;
        }

        // Один город — одна строка: «Stockholm - Kista» и «Stockholm - Bromma» объединяются (берём первый адрес)
        static void AddCity(Dictionary<string, PingTarget> map, string city, string ip)
        {
            IPAddress a;
            if (map.ContainsKey(city) || !IPAddress.TryParse(ip, out a)) return;
            PingTarget t = new PingTarget(); t.Name = city; t.Ip = a; t.Host = ip;
            map[city] = t;
        }

        public static string City(string desc)
        {
            string s = desc;
            int p = s.IndexOf(" ("); if (p > 0) s = s.Substring(0, p);
            foreach (string pre in new[] { "Alibaba Cloud ", "Tencent ", "Hetzner ", "Datapacket " }) if (s.StartsWith(pre)) s = s.Substring(pre.Length);
            string tail = "";
            p = s.IndexOf(" - ");
            if (p > 0) { tail = s.Substring(p + 3); s = s.Substring(0, p); }
            string ru;
            if (Ru.TryGetValue(s, out ru)) s = ru;
            if (tail == "Mobile" || tail == "Telecom" || tail == "Unicom") s += " (" + tail + ")";   // разные операторы Китая
            return s;
        }

        static string ValveConfig(int appid)
        {
            string file = Path.Combine(CacheDir, "sdr-" + appid + ".json");
            try { if (File.Exists(file) && (DateTime.Now - File.GetLastWriteTime(file)).TotalDays < 7) return File.ReadAllText(file, Encoding.UTF8); } catch { }
            try
            {
                try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }   // TLS 1.2
                using (WebClient w = new WebClient())
                {
                    w.Headers[HttpRequestHeader.UserAgent] = "Tunex/" + Theme.Version;
                    w.Encoding = Encoding.UTF8;
                    string json = w.DownloadString("https://api.steampowered.com/ISteamApps/GetSDRConfig/v1/?appid=" + appid);
                    if (json.Contains("\"pops\""))
                    {
                        try { Directory.CreateDirectory(CacheDir); File.WriteAllText(file, json, Encoding.UTF8); } catch { }
                        return json;
                    }
                }
            }
            catch { }
            try { if (File.Exists(file)) return File.ReadAllText(file, Encoding.UTF8); } catch { }   // старый кэш лучше, чем ничего
            return null;
        }

        // Роутер (основной шлюз) — чтобы понять, виновата домашняя сеть или интернет
        public static PingTarget Router()
        {
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (GatewayIPAddressInformation g in ni.GetIPProperties().GatewayAddresses)
                    {
                        if (g.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || g.Address.Equals(IPAddress.Any)) continue;
                        PingTarget t = new PingTarget(); t.Name = "Ваш роутер"; t.Ip = g.Address; t.Host = g.Address.ToString(); t.Router = true;
                        return t;
                    }
                }
            }
            catch { }
            return null;
        }

        // Полная проверка: быстрый пинг всех серверов → 6 ближайших → по 20 пингов к каждому и к роутеру
        public const int Count = 20, Nearest = 6;

        public static List<PingTarget> Run(string game, out PingTarget router)
        {
            List<PingTarget> all = Servers(game);
            Parallel(all, delegate(PingTarget t)
            {
                if (t.Ip == null) { try { foreach (IPAddress a in Dns.GetHostAddresses(t.Host)) if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) { t.Ip = a; break; } } catch { } }
                if (t.Ip != null) Measure(t, 2, 900);
            });
            all.RemoveAll(delegate(PingTarget t) { return t.Rtt.Count == 0; });
            all.Sort(delegate(PingTarget a, PingTarget b) { return a.Avg.CompareTo(b.Avg); });
            if (all.Count > Nearest) all.RemoveRange(Nearest, all.Count - Nearest);

            List<PingTarget> fine = new List<PingTarget>();
            foreach (PingTarget t in all) { PingTarget n = new PingTarget(); n.Name = t.Name; n.Host = t.Host; n.Ip = t.Ip; fine.Add(n); }
            router = Router();
            List<PingTarget> run = new List<PingTarget>(fine);
            if (router != null) run.Add(router);
            Parallel(run, delegate(PingTarget t) { Measure(t, Count, 1000); });
            fine.Sort(delegate(PingTarget a, PingTarget b) { return (a.Ok ? a.Avg : 9999).CompareTo(b.Ok ? b.Avg : 9999); });
            return fine;
        }

        static void Measure(PingTarget t, int count, int timeout)
        {
            using (Ping p = new Ping())
                for (int i = 0; i < count; i++)
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    t.Sent++;
                    try
                    {
                        PingReply r = p.Send(t.Ip, timeout);
                        if (r.Status == IPStatus.Success) t.Rtt.Add((int)r.RoundtripTime); else t.Lost++;
                    }
                    catch { t.Lost++; }
                    int wait = 120 - (int)sw.ElapsedMilliseconds;
                    if (wait > 0 && i < count - 1) Thread.Sleep(wait);
                }
        }

        static void Parallel(List<PingTarget> list, Action<PingTarget> work)
        {
            List<Thread> threads = new List<Thread>();
            foreach (PingTarget t in list)
            {
                PingTarget x = t;
                Thread th = new Thread(delegate() { try { work(x); } catch { } });
                th.IsBackground = true; th.Start(); threads.Add(th);
            }
            foreach (Thread th in threads) th.Join(30000);
        }

        public static string Ms(float v) { return v < 0 ? "—" : Math.Round(v).ToString(CultureInfo.InvariantCulture) + " мс"; }
        public static string Pct(float v) { return Math.Round(v).ToString(CultureInfo.InvariantCulture) + "%"; }

        // Оценка одного сервера
        public static string Grade(PingTarget t, out System.Drawing.Color c)
        {
            if (!t.Ok) { c = Theme.Red; return "Не отвечает"; }
            if (t.Unstable) { c = Theme.Red; return "Нестабильно"; }
            if (t.Router) { c = Theme.Green; return "В порядке"; }
            float a = t.Avg;
            if (a <= 50) { c = Theme.Green; return "Отлично"; }
            if (a <= 80) { c = Theme.Green; return "Хорошо"; }
            if (a <= 120) { c = Theme.Orange; return "Терпимо"; }
            c = Theme.Red; return "Далеко";
        }

        // Итог простыми словами
        public static string Verdict(string game, List<PingTarget> servers, PingTarget router, out string text, out System.Drawing.Color c)
        {
            PingTarget best = null;
            foreach (PingTarget t in servers) if (t.Ok && (best == null || t.Avg < best.Avg)) best = t;
            bool homeBad = router != null && router.Ok && router.Unstable;
            if (best == null)
            {
                c = Theme.Red;
                text = homeBad ? "Даже до роутера пинг скачет или теряется — проблема в домашней сети." :
                    "Возможно, нет интернета, или пинг блокирует антивирус, VPN или сеть (например, рабочая).";
                return "Серверы " + Title(game) + " не ответили";
            }
            string where = "Ближайший сервер — " + best.Name + ": " + Ms(best.Avg) + ", скачки " + Ms(best.Jitter) + ", потери " + Pct(best.Loss) + ".";
            if (homeBad)
            {
                c = Theme.Red;
                text = where + " Пинг скачет уже до роутера (" + Ms(router.Avg) + ", скачки " + Ms(router.Jitter) + ", потери " + Pct(router.Loss) + "), " +
                    "значит дело в Wi-Fi или роутере, а не у провайдера. Подключите кабель или сядьте ближе к роутеру, перезагрузите роутер.";
                return "Проблема в домашней сети";
            }
            if (best.Unstable)
            {
                c = Theme.Orange;
                text = where + " До роутера всё ровно, значит компьютер и Wi-Fi ни при чём — нестабильно на пути до сервера, у провайдера. " +
                    "Проверьте ещё раз в другое время; если повторяется — сообщите провайдеру.";
                return "Нестабильно на пути до сервера";
            }
            if (best.Avg <= 50) { c = Theme.Green; text = where + " Это отличный пинг для онлайн-игр."; return "Интернет отлично подходит для " + Title(game); }
            if (best.Avg <= 80) { c = Theme.Green; text = where + " Играть комфортно."; return "Интернет хорошо подходит для " + Title(game); }
            if (best.Avg <= 120) { c = Theme.Orange; text = where + " Играть можно, но задержка будет ощутима в перестрелках. Сеть стабильная — просто сервер далеко."; return "Пинг терпимый"; }
            c = Theme.Red; text = where + " Ближайший сервер далеко — компьютер и сеть тут не помогут. Выбирайте этот регион в настройках поиска игры."; return "Пинг высокий";
        }
    }

    // ================================================================ Не беспокоить во время игры
    static class QuietMode
    {
        // Фокусировка внимания / «Не беспокоить»: 0 — выкл., 1 — только приоритет, 2 — только будильники
        static ulong FocusState = 0x0D83063EA3BF1C75;
        [DllImport("ntdll.dll")] static extern int NtQueryWnfStateData(ref ulong state, IntPtr type, IntPtr scope, out uint stamp, byte[] buf, ref uint size);
        [DllImport("ntdll.dll")] static extern int NtUpdateWnfStateData(ref ulong state, byte[] buf, int len, IntPtr type, IntPtr scope, uint matchStamp, int checkStamp);

        public static int ReadFocus()
        {
            try
            {
                byte[] b = new byte[4]; uint size = 4, stamp;
                if (NtQueryWnfStateData(ref FocusState, IntPtr.Zero, IntPtr.Zero, out stamp, b, ref size) != 0) return -1;
                return size == 0 ? 0 : BitConverter.ToInt32(b, 0);
            }
            catch { return -1; }
        }

        public static bool SetFocus(int v)
        {
            try
            {
                byte[] b = BitConverter.GetBytes(v);
                return NtUpdateWnfStateData(ref FocusState, b, 4, IntPtr.Zero, IntPtr.Zero, 0, 0) == 0 && ReadFocus() == v;
            }
            catch { return false; }
        }

        // Пауза обновлений Windows — те же значения, что пишет кнопка «Приостановить» в Параметрах.
        // Срок — сутки: если Tunex закроется во время игры, пауза снимется сама.
        const string Ux = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
        static readonly string[] PauseNames = { "PauseUpdatesStartTime", "PauseUpdatesExpiryTime", "PauseFeatureUpdatesStartTime",
            "PauseFeatureUpdatesEndTime", "PauseQualityUpdatesStartTime", "PauseQualityUpdatesEndTime" };

        static bool AlreadyPaused()
        {
            try
            {
                string exp = Convert.ToString(Reg.Get("HKLM", Ux, "PauseUpdatesExpiryTime"));
                DateTime t;
                return !string.IsNullOrEmpty(exp) && DateTime.TryParse(exp, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t) && t > DateTime.UtcNow;
            }
            catch { return false; }
        }

        public static bool Active { get { return App.Manager.State.QuietSnap != null; } }

        // Включить на время игры. Возвращает, что удалось: «уведомления», «обновления»
        public static List<string> Begin()
        {
            List<string> done = new List<string>();
            AppState s = App.Manager.State;
            if (s.QuietSnap != null) return done;
            StringBuilder snap = new StringBuilder();
            int focus = ReadFocus();
            if (focus >= 0 && focus != 2 && SetFocus(2)) { snap.Append("focus=" + focus + ";"); done.Add("уведомления"); }
            if (SysInfo.IsAdmin && !AlreadyPaused())
            {
                try
                {
                    foreach (string n in PauseNames)
                    {
                        object old = Reg.Get("HKLM", Ux, n);
                        if (old != null) snap.Append("old:" + n + "=" + Convert.ToString(old) + ";");
                    }
                    string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                    string end = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                    for (int i = 0; i < PauseNames.Length; i++)
                        Reg.Set("HKLM", Ux, PauseNames[i], i % 2 == 0 ? now : end, RegistryValueKind.String);
                    snap.Append("upd=1;");
                    done.Add("обновления");
                }
                catch { }
            }
            s.QuietSnap = snap.ToString();
            s.Save();
            return done;
        }

        // Вернуть как было (и после сбоя — при следующем запуске Tunex)
        public static void End()
        {
            AppState s = App.Manager.State;
            string snap = s.QuietSnap;
            if (snap == null) return;
            foreach (string part in snap.Split(';'))
            {
                if (part.StartsWith("focus=")) { int v; if (int.TryParse(part.Substring(6), out v)) SetFocus(v); }
            }
            if (snap.Contains("upd=1;"))
            {
                try
                {
                    foreach (string n in PauseNames) Reg.Delete("HKLM", Ux, n);
                    foreach (string part in snap.Split(';'))
                        if (part.StartsWith("old:"))
                        {
                            string kv = part.Substring(4); int eq = kv.IndexOf('=');
                            if (eq > 0) Reg.Set("HKLM", Ux, kv.Substring(0, eq), kv.Substring(eq + 1), RegistryValueKind.String);
                        }
                }
                catch { }
            }
            s.QuietSnap = null;
            s.Save();
        }
    }
}
