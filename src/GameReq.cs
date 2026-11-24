using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Tunex
{
    // ---------------------------------------------------------------- Игры Steam на этом ПК
    class SteamGame { public int AppId; public string Name; public bool Installed; }

    static class SteamLib
    {
        // Служебные пакеты Steam — это не игры
        static readonly int[] NotGames = { 228980, 1070560, 1391110, 1628350, 1493710, 1826330, 2180100, 250820, 323910, 1007, 1161040, 1245040, 1420170, 1887720, 2348590 };

        public static string Root()
        {
            string over = Environment.GetEnvironmentVariable("TUNEX_STEAM_DIR");
            if (!string.IsNullOrEmpty(over)) return over;
            string p = Convert.ToString(Reg.Get("HKCU", @"Software\Valve\Steam", "SteamPath"));
            if (string.IsNullOrEmpty(p)) p = Convert.ToString(Reg.Get("HKLM", @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"));
            if (string.IsNullOrEmpty(p)) return null;
            p = p.Replace('/', '\\');
            return Directory.Exists(p) ? p : null;
        }

        public static bool Present { get { return Root() != null; } }

        // Папки библиотек: основная + перечисленные в libraryfolders.vdf
        public static List<string> Libraries()
        {
            List<string> libs = new List<string>();
            string root = Root();
            if (root == null) return libs;
            libs.Add(root);
            foreach (string vdf in new string[] { Path.Combine(root, @"steamapps\libraryfolders.vdf"), Path.Combine(root, @"config\libraryfolders.vdf") })
            {
                if (!File.Exists(vdf)) continue;
                string text;
                try { text = File.ReadAllText(vdf, Encoding.UTF8); } catch { continue; }
                foreach (Match m in Regex.Matches(text, "\"(?:path|\\d+)\"\\s+\"([^\"]+)\""))
                {
                    string p = m.Groups[1].Value.Replace("\\\\", "\\");
                    if (p.Length < 3 || !Directory.Exists(p)) continue;
                    bool dup = false;
                    foreach (string l in libs) if (string.Equals(Path.GetFullPath(l).TrimEnd('\\'), Path.GetFullPath(p).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) dup = true;
                    if (!dup) libs.Add(p);
                }
            }
            return libs;
        }

        public static List<SteamGame> Installed()
        {
            Dictionary<int, SteamGame> map = new Dictionary<int, SteamGame>();
            foreach (string lib in Libraries())
            {
                string apps = Path.Combine(lib, "steamapps");
                if (!Directory.Exists(apps)) continue;
                string[] files;
                try { files = Directory.GetFiles(apps, "appmanifest_*.acf"); } catch { continue; }
                foreach (string f in files)
                {
                    string t;
                    try { t = File.ReadAllText(f, Encoding.UTF8); } catch { continue; }
                    Match id = Regex.Match(t, "\"appid\"\\s+\"(\\d+)\"", RegexOptions.IgnoreCase);
                    Match name = Regex.Match(t, "\"name\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);
                    if (!id.Success) continue;
                    int appid = int.Parse(id.Groups[1].Value, CultureInfo.InvariantCulture);
                    string nm = name.Success ? name.Groups[1].Value : "Игра " + appid;
                    if (Array.IndexOf(NotGames, appid) >= 0 || IsToolName(nm) || map.ContainsKey(appid)) continue;
                    SteamGame g = new SteamGame(); g.AppId = appid; g.Name = nm; g.Installed = true;
                    map[appid] = g;
                }
            }
            List<SteamGame> list = new List<SteamGame>(map.Values);
            list.Sort(delegate(SteamGame a, SteamGame b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); });
            return list;
        }

        static bool IsToolName(string n)
        {
            string u = n.ToUpperInvariant();
            return u.StartsWith("PROTON") || u.Contains("REDISTRIBUTABLE") || u.Contains("STEAM LINUX RUNTIME") || u.Contains("STEAMVR") || u.Contains("DEDICATED SERVER") || u.EndsWith(" SDK");
        }
    }

    // ---------------------------------------------------------------- Системные требования из Steam
    class ReqLevel
    {
        public string CpuText, GpuText, RamText;
        public double RamGb;                         // 0 — не указано
        public int Gpu = -1, Cpu = -1;               // самая слабая из перечисленных моделей; -1 — модели не названы
        public string GpuModels = "", CpuModels = "";
    }

    class GameReq
    {
        public int AppId; public string Name, Type, Error;
        public ReqLevel Min, Rec;
        public bool HasAny { get { return Min != null || Rec != null; } }
    }

    static class SteamReq
    {
        const int CacheDays = 14;

        static string CacheDir { get { return Path.Combine(AppState.Dir, "steam"); } }

        static string Download(string url)
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }   // TLS 1.2
            HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = "Tunex/" + Theme.Version;
            r.Timeout = 12000; r.ReadWriteTimeout = 12000;
            r.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            using (WebResponse resp = r.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }

        // Требования игры: из кэша (до 14 дней), иначе из Steam; без интернета — из старого кэша
        public static GameReq Get(int appid, out bool fromNet)
        {
            fromNet = false;
            string file = Path.Combine(CacheDir, appid + ".json");
            string json = null;
            bool fresh = File.Exists(file) && (DateTime.Now - File.GetLastWriteTime(file)).TotalDays < CacheDays;
            if (fresh) try { json = File.ReadAllText(file, Encoding.UTF8); } catch { }
            if (json == null)
            {
                try
                {
                    json = Download("https://store.steampowered.com/api/appdetails?appids=" + appid + "&l=english");
                    fromNet = true;
                    if (json.Contains("\"success\":true"))
                        try { Directory.CreateDirectory(CacheDir); File.WriteAllText(file, json, Encoding.UTF8); } catch { }
                }
                catch (Exception ex)
                {
                    if (File.Exists(file)) try { json = File.ReadAllText(file, Encoding.UTF8); } catch { }
                    if (json == null)
                    {
                        GameReq e = new GameReq(); e.AppId = appid;
                        e.Error = "Не удалось связаться со Steam" + (ex is WebException ? " — проверьте интернет" : ": " + ex.Message);
                        return e;
                    }
                }
            }
            return Parse(appid, json);
        }

        public static GameReq Parse(int appid, string json)
        {
            GameReq g = new GameReq(); g.AppId = appid;
            Dictionary<string, object> root = null;
            try { root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>; } catch { }
            Dictionary<string, object> entry = root != null && root.ContainsKey(appid.ToString()) ? root[appid.ToString()] as Dictionary<string, object> : null;
            if (entry == null || !(entry.ContainsKey("success") && entry["success"] is bool && (bool)entry["success"]))
            {
                g.Error = "Steam не отдаёт страницу этой игры (возможно, её убрали из магазина или она недоступна в регионе)";
                return g;
            }
            Dictionary<string, object> data = entry["data"] as Dictionary<string, object>;
            if (data == null) { g.Error = "Steam прислал пустой ответ"; return g; }
            g.Name = Str(data, "name");
            g.Type = Str(data, "type");
            Dictionary<string, object> pc = data.ContainsKey("pc_requirements") ? data["pc_requirements"] as Dictionary<string, object> : null;
            if (pc != null)
            {
                g.Min = ParseLevel(Str(pc, "minimum"));
                g.Rec = ParseLevel(Str(pc, "recommended"));
            }
            return g;
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object o; return d.TryGetValue(key, out o) && o != null ? Convert.ToString(o, CultureInfo.InvariantCulture) : null;
        }

        // HTML Steam: <li><strong>Processor:</strong> Intel Core i5-6600<br></li> ...
        public static ReqLevel ParseLevel(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;
            string t = Regex.Replace(html, "<br\\s*/?>|</li>|<li>|</p>|</ul>", "\n", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, "<[^>]+>", " ");
            t = WebUtility.HtmlDecode(t);
            ReqLevel r = new ReqLevel();
            bool any = false;
            foreach (string raw in t.Split('\n'))
            {
                Match m = Regex.Match(raw, "^\\s*([A-Za-z][A-Za-z /&]{1,30}?)\\s*:\\s*(.+?)\\s*$");
                if (!m.Success) continue;
                string key = m.Groups[1].Value.Trim().ToLowerInvariant(), val = m.Groups[2].Value;
                if (key == "processor" || key == "cpu" || key == "processors")
                { r.CpuText = val; any = true; }
                else if (key == "memory" || key == "ram" || key == "system memory")
                {
                    r.RamText = val; any = true;
                    Match g = Regex.Match(val, "(\\d+(?:[.,]\\d+)?)\\s*(GB|MB|G\\b|ГБ)", RegexOptions.IgnoreCase);
                    if (g.Success)
                    {
                        double v = double.Parse(g.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
                        r.RamGb = g.Groups[2].Value.ToUpperInvariant() == "MB" ? v / 1024.0 : v;
                    }
                }
                else if (key == "graphics" || key == "video card" || key == "video" || key == "gpu" || key == "graphics card" || key == "video cards")
                { r.GpuText = val; any = true; }
            }
            if (!any) return null;
            Lowest(GpuDb.FindInText(r.GpuText), out r.Gpu, out r.GpuModels);
            Lowest(CpuDb.FindInText(r.CpuText), out r.Cpu, out r.CpuModels);
            return r;
        }

        // Из перечисленных вариантов («GTX 1060 или RX 470») подходит любой — берём самый слабый
        static void Lowest(List<KeyValuePair<string, int>> list, out int score, out string names)
        {
            score = -1; names = "";
            List<string> n = new List<string>();
            foreach (KeyValuePair<string, int> kv in list)
            {
                if (score < 0 || kv.Value < score) score = kv.Value;
                if (!n.Contains(kv.Key)) n.Add(kv.Key);
            }
            names = string.Join(" / ", n.ToArray());
        }

        // Поиск по магазину Steam: только название, без данных о пользователе
        public static List<SteamGame> Search(string term)
        {
            List<SteamGame> list = new List<SteamGame>();
            string json = Download("https://store.steampowered.com/api/storesearch/?term=" + Uri.EscapeDataString(term) + "&l=english&cc=US");
            Dictionary<string, object> root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            object items;
            if (root == null || !root.TryGetValue("items", out items) || !(items is IEnumerable)) return list;
            foreach (object o in (IEnumerable)items)
            {
                Dictionary<string, object> it = o as Dictionary<string, object>;
                if (it == null || !it.ContainsKey("id")) continue;
                SteamGame g = new SteamGame();
                g.AppId = Convert.ToInt32(it["id"], CultureInfo.InvariantCulture);
                g.Name = Str(it, "name") ?? ("Игра " + g.AppId);
                list.Add(g);
                if (list.Count >= 6) break;
            }
            return list;
        }
    }

    // ---------------------------------------------------------------- Процессоры: примерная игровая оценка по названию
    // Шкала: Core i5-12400 ≈ 135. Учитывается поколение (скорость одного ядра) и число потоков.
    static class CpuDb
    {
        static double IntelSt(int gen)
        {
            double[] st = { 0, 55, 68, 72, 80, 81, 88, 94, 97, 101, 104, 115, 135, 145, 148 };
            return gen >= 1 && gen < st.Length ? st[gen] : gen >= st.Length ? 148 : 55;
        }

        // Сколько потоков реально используют игры: 2 → 0.55, 4 → 0.75, 8 → 0.92, 12 → 1.0, 16 → 1.04
        static double ThreadFactor(int t)
        {
            int[] x = { 1, 2, 4, 6, 8, 12, 16, 24 };
            double[] y = { 0.40, 0.55, 0.75, 0.85, 0.92, 1.00, 1.04, 1.07 };
            if (t <= x[0]) return y[0];
            for (int i = 1; i < x.Length; i++)
                if (t <= x[i]) return y[i - 1] + (y[i] - y[i - 1]) * (t - x[i - 1]) / (double)(x[i] - x[i - 1]);
            return y[y.Length - 1];
        }

        static int IntelThreads(int tier, int gen, bool lowPower)
        {
            if (gen <= 7) return tier == 3 ? 4 : tier == 5 ? (lowPower ? 4 : 4) : 8;
            if (gen == 8) return tier == 3 ? 4 : tier == 5 ? (lowPower ? 8 : 6) : tier == 7 ? (lowPower ? 8 : 12) : 16;
            if (gen == 9) return tier == 3 ? 4 : tier == 5 ? 6 : tier == 7 ? 8 : 16;
            if (gen <= 11) return tier == 3 ? 8 : tier == 5 ? (lowPower ? 8 : 12) : tier == 7 ? (lowPower ? 8 : 16) : 20;
            return tier == 3 ? 8 : tier == 5 ? (lowPower ? 12 : 16) : tier == 7 ? (lowPower ? 12 : 20) : 28;
        }

        class Hit { public string Model; public double St; public int Threads; }

        static string Normalize(string s)
        {
            string n = s.ToUpperInvariant().Replace("(TM)", " ").Replace("(R)", " ").Replace("™", " ").Replace("®", " ");
            return Regex.Replace(n, "\\s+", " ").Trim();
        }

        static List<Hit> Parse(string text)
        {
            List<Hit> hits = new List<Hit>();
            if (string.IsNullOrEmpty(text)) return hits;
            string n = Normalize(text);
            // Intel Core Ultra 5/7/9 1xxH (Meteor Lake), 2xxV (Lunar Lake), 2xxK/HX (Arrow Lake)
            foreach (Match x in Regex.Matches(n, "CORE\\s*ULTRA\\s*([579])\\s*-?\\s*(\\d)(\\d{2})([A-Z]{0,2})"))
            {
                int tier = x.Groups[1].Value[0] - '0'; string suf = x.Groups[4].Value;
                Hit h = new Hit(); h.Model = "Core Ultra " + tier + " " + x.Groups[2].Value + x.Groups[3].Value + suf;
                if (x.Groups[2].Value == "1") { h.St = 128 * (suf.StartsWith("U") ? 0.85 : 1); h.Threads = suf.StartsWith("U") ? 12 : tier == 5 ? 18 : 22; }
                else if (suf == "V") { h.St = 132; h.Threads = 8; }
                else { h.St = suf.StartsWith("H") ? 145 : suf.StartsWith("U") ? 125 : 152; h.Threads = tier == 5 ? 14 : tier == 7 ? 20 : 24; }
                hits.Add(h);
            }
            n = Regex.Replace(n, "CORE\\s*ULTRA\\s*[579]\\s*-?\\s*\\d{3}", "#");

            // Intel Core i3/i5/i7/i9 — 920, 2500K, 4770, 1165G7, 12400F
            foreach (Match x in Regex.Matches(n, "(?<![A-Z0-9])I([3579])\\s*-?\\s*(\\d{3,5})([A-Z]{0,2}\\d?)(?![0-9])"))
            {
                int tier = x.Groups[1].Value[0] - '0'; string num = x.Groups[2].Value, suf = x.Groups[3].Value;
                int gen;
                if (num.Length == 5) gen = int.Parse(num.Substring(0, 2));
                else if (num.Length == 4) gen = num[0] == '1' ? 10 + (num[1] - '0') : num[0] - '0';
                else gen = 1;
                if (gen > 14 || gen < 1) continue;
                bool lowPower = suf.StartsWith("U") || suf.StartsWith("Y") || suf.StartsWith("G") || (suf.StartsWith("M") && !suf.StartsWith("MQ") && !suf.StartsWith("MX"));
                double mult = lowPower ? (suf.StartsWith("P") ? 0.88 : 0.82)
                    : suf.StartsWith("H") || suf.StartsWith("MQ") || suf.StartsWith("MX") ? 0.93
                    : suf.StartsWith("K") || suf == "X" || suf.StartsWith("XE") ? 1.04
                    : suf.StartsWith("T") ? 0.9 : 1.0;
                if (suf.StartsWith("P")) { lowPower = true; mult = 0.88; }
                Hit h = new Hit(); h.Model = "Core i" + tier + "-" + num + suf;
                h.St = IntelSt(gen) * mult;
                h.Threads = IntelThreads(tier, gen, lowPower);
                if (gen == 1 && tier == 5 && num.StartsWith("6")) h.Threads = 4;   // i5-6xx первого поколения: 2 ядра
                hits.Add(h);
            }

            // Xeon E3-1230 v2 и подобные — по сути Core i7 своего поколения
            foreach (Match x in Regex.Matches(n, "E3\\s*-?\\s*12(\\d)(\\d)L?\\s*(?:V(\\d))?"))
            {
                int v = x.Groups[3].Success ? int.Parse(x.Groups[3].Value) : 1;
                Hit h = new Hit(); h.Model = "Xeon E3-12" + x.Groups[1].Value + x.Groups[2].Value + (v > 1 ? " v" + v : "");
                h.St = IntelSt(v + 1) * 0.97; h.Threads = x.Groups[1].Value == "2" ? 4 : 8;
                hits.Add(h);
            }

            // AMD Ryzen AI 5/7/9 (Zen 5, ноутбуки)
            foreach (Match x in Regex.Matches(n, "RYZEN\\s*AI\\s*(?:MAX\\+?\\s*)?(?:PRO\\s*)?([3579])\\s*(?:HX\\s*|PRO\\s*)?(\\d{3})"))
            {
                int tier = x.Groups[1].Value[0] - '0';
                Hit h = new Hit(); h.Model = "Ryzen AI " + tier + " " + x.Groups[2].Value; h.St = 145; h.Threads = tier == 5 ? 12 : tier == 7 ? 16 : 24;
                hits.Add(h);
            }
            n = Regex.Replace(n, "RYZEN\\s*AI[^,/]*?\\d{3}", "#");

            // AMD Ryzen 3/5/7/9 ####
            foreach (Match x in Regex.Matches(n, "RYZEN\\s*([3579])\\s*(?:PRO\\s*)?(\\d{4})([A-Z0-9]{0,4})"))
            {
                int tier = x.Groups[1].Value[0] - '0'; string num = x.Groups[2].Value, suf = x.Groups[3].Value;
                int series = num[0] - '0';
                bool apu = suf.StartsWith("G"), mobile = suf.StartsWith("U") || suf.StartsWith("H");
                int zen; // 1 = Zen, 15 = Zen+, 2, 3, 4, 5
                switch (series)
                {
                    case 1: zen = 1; break;
                    case 2: zen = apu || mobile ? 1 : 15; break;
                    case 3: zen = apu || mobile ? 15 : 2; break;
                    case 4: zen = 2; break;
                    case 5: zen = 3; break;
                    case 6: zen = 3; break;
                    case 7: { char d = num[2]; zen = d == '2' ? 2 : d == '3' ? 3 : 4; break; }
                    case 8: zen = 4; break;
                    case 9: zen = 5; break;
                    default: continue;
                }
                double st = zen == 1 ? 76 : zen == 15 ? 80 : zen == 2 ? 95 : zen == 3 ? 118 : zen == 4 ? 140 : 150;
                double mult = suf.Contains("X3D") ? 1.15 : suf.StartsWith("U") ? 0.85 : suf.StartsWith("H") ? 0.95 : apu ? 0.97 : suf.StartsWith("X") ? 1.04 : 1.0;
                int th = tier == 3 ? (zen <= 15 ? 4 : 8)
                    : tier == 5 ? ((zen <= 15 && (num.EndsWith("400") || num.EndsWith("500") || apu || mobile)) ? 8 : 12)
                    : tier == 7 ? (zen <= 15 && mobile ? 8 : 16)
                    : (num.EndsWith("950") ? 32 : 24);
                Hit h = new Hit(); h.Model = "Ryzen " + tier + " " + num + suf; h.St = st * mult; h.Threads = th;
                hits.Add(h);
            }

            // AMD FX-4300 / 6300 / 8350 / 9590: первая цифра — число потоков
            foreach (Match x in Regex.Matches(n, "(?<![A-Z])FX\\s*-?\\s*([4689])(\\d)(\\d{2})"))
            {
                Hit h = new Hit(); h.Model = "FX-" + x.Groups[1].Value + x.Groups[2].Value + x.Groups[3].Value;
                int first = x.Groups[1].Value[0] - '0';
                h.Threads = first == 9 ? 8 : first;
                h.St = first == 9 ? 58 : x.Groups[2].Value == "1" ? 45 : 52;
                hits.Add(h);
            }

            // Phenom II X4, Athlon II X2, Athlon X4 860K, Athlon 3000G
            foreach (Match x in Regex.Matches(n, "PHENOM\\s*(II\\s*)?X\\s*(\\d)"))
            { Hit h = new Hit(); h.Model = "Phenom " + (x.Groups[1].Success ? "II " : "") + "X" + x.Groups[2].Value; h.St = x.Groups[1].Success ? 45 : 38; h.Threads = x.Groups[2].Value[0] - '0'; hits.Add(h); }
            foreach (Match x in Regex.Matches(n, "ATHLON\\s*(II\\s*)?(?:64\\s*)?X\\s*(\\d)"))
            { Hit h = new Hit(); h.Model = "Athlon " + (x.Groups[1].Success ? "II " : "") + "X" + x.Groups[2].Value; h.St = x.Groups[1].Success ? 35 : 48; h.Threads = x.Groups[2].Value[0] - '0'; hits.Add(h); }
            foreach (Match x in Regex.Matches(n, "ATHLON\\s*(?:GOLD\\s*|SILVER\\s*)?(\\d{3,4})(GE|G|U)"))
            { Hit h = new Hit(); h.Model = "Athlon " + x.Groups[1].Value + x.Groups[2].Value; h.St = x.Groups[2].Value == "U" ? 62 : 72; h.Threads = 4; hits.Add(h); }

            // Core 2 Duo / Quad
            foreach (Match x in Regex.Matches(n, "CORE\\s*2\\s*(DUO|QUAD|EXTREME)|CORE2\\s*(DUO|QUAD)"))
            {
                string kind = x.Groups[1].Success ? x.Groups[1].Value : x.Groups[2].Value;
                Hit h = new Hit(); h.Model = "Core 2 " + (kind == "QUAD" ? "Quad" : kind == "DUO" ? "Duo" : "Extreme"); h.St = 38; h.Threads = kind == "QUAD" ? 4 : 2; hits.Add(h);
            }

            // Pentium G4560 / Gold G5400, Celeron G3900
            foreach (Match x in Regex.Matches(n, "(PENTIUM|CELERON)\\s*(?:GOLD\\s*)?G\\s*-?(\\d)(\\d{3})"))
            {
                int d = x.Groups[2].Value[0] - '0';
                int gen = d <= 2 ? 3 : d == 3 ? 4 : d == 4 ? 6 : d == 5 ? 8 : d == 6 ? 10 : 12;
                bool pentium = x.Groups[1].Value == "PENTIUM";
                Hit h = new Hit(); h.Model = (pentium ? "Pentium G" : "Celeron G") + x.Groups[2].Value + x.Groups[3].Value;
                h.St = IntelSt(gen) * (pentium ? 0.92 : 0.82);
                h.Threads = pentium && (gen >= 8 || (gen >= 6 && int.Parse(x.Groups[3].Value) >= 560)) ? 4 : 2;
                hits.Add(h);
            }
            // Слабые ноутбучные Celeron/Pentium N/J и Intel N100
            foreach (Match x in Regex.Matches(n, "(?:CELERON|PENTIUM)\\s*(?:SILVER\\s*)?([NJ]\\d{4})"))
            { Hit h = new Hit(); h.Model = x.Groups[1].Value; h.St = 38; h.Threads = 4; hits.Add(h); }
            foreach (Match x in Regex.Matches(n, "(?<![A-Z0-9])(N(?:50|95|97|100|150|200|300|305))(?![0-9])"))
            { Hit h = new Hit(); h.Model = "Intel " + x.Groups[1].Value; h.St = 70; h.Threads = x.Groups[1].Value.StartsWith("N3") ? 8 : 4; hits.Add(h); }

            return hits;
        }

        static int Score(Hit h, int threads) { return (int)Math.Round(h.St * ThreadFactor(threads > 0 ? threads : h.Threads)); }

        public static List<KeyValuePair<string, int>> FindInText(string text)
        {
            List<KeyValuePair<string, int>> r = new List<KeyValuePair<string, int>>();
            foreach (Hit h in Parse(text)) r.Add(new KeyValuePair<string, int>(h.Model, Score(h, 0)));
            return r;
        }

        // Процессор этого ПК: настоящее число потоков из Windows точнее нашей догадки
        public static int ScoreOf(string name, int realThreads, out string model)
        {
            model = null;
            List<Hit> hits = Parse(name);
            if (hits.Count == 0) return -1;
            model = hits[0].Model;
            return Score(hits[0], realThreads);
        }
    }

    // ---------------------------------------------------------------- Сравнение ПК с требованиями
    class PcProfile
    {
        public int Gpu = -1, Cpu = -1;
        public string GpuName, CpuName;
        public double RamGb;

        public static PcProfile From(PcSpecs s)
        {
            PcProfile p = new PcProfile();
            foreach (GpuInfo g in s.Gpus)
                if (g.Known && g.Score > p.Gpu) { p.Gpu = g.Score; p.GpuName = g.Name; }
            string m;
            p.Cpu = CpuDb.ScoreOf(s.Cpu ?? "", s.Threads, out m);
            p.CpuName = m ?? s.Cpu;
            p.RamGb = s.RamTotal / 1073741824.0;
            return p;
        }
    }

    enum Fit { NoData, Below, Minimum, Recommended }

    class PartResult { public string Part; public Fit Fit; public string Text; }

    class GameVerdict
    {
        public Fit Fit; public string Title; public List<PartResult> Parts = new List<PartResult>();

        const double Slack = 0.92;   // оценки примерные: разница до 8% — «на уровне»

        static PartResult Compare(string part, int mine, int min, int rec, string minNames, string recNames, string unitName)
        {
            PartResult r = new PartResult(); r.Part = part;
            if (mine < 0) { r.Fit = Fit.NoData; r.Text = "вашу модель Tunex не знает — сравнить не может"; return r; }
            if (min < 0 && rec < 0) { r.Fit = Fit.NoData; r.Text = "в требованиях нет конкретной модели — сравнить не с чем"; return r; }
            if (rec >= 0 && mine >= rec * Slack) { r.Fit = Fit.Recommended; r.Text = "на уровне рекомендуемой (" + recNames + ") или выше"; return r; }
            if (min >= 0 && mine < min * Slack) { r.Fit = Fit.Below; r.Text = "слабее минимальной: нужна " + minNames; return r; }
            if (min >= 0) { r.Fit = Fit.Minimum; r.Text = "не ниже минимальной (" + minNames + ")" + (rec >= 0 ? ", но слабее рекомендуемой (" + recNames + ")" : ""); return r; }
            r.Fit = Fit.NoData; r.Text = "слабее рекомендуемой (" + recNames + "), а минимальная модель не указана"; return r;
        }

        public static GameVerdict Make(PcProfile pc, GameReq g)
        {
            GameVerdict v = new GameVerdict();
            ReqLevel mn = g.Min ?? new ReqLevel(), rc = g.Rec ?? new ReqLevel();

            PartResult gpu = Compare("Видеокарта", pc.Gpu, mn.Gpu, rc.Gpu, mn.GpuModels, rc.GpuModels, null);
            PartResult cpu = Compare("Процессор", pc.Cpu, mn.Cpu, rc.Cpu, mn.CpuModels, rc.CpuModels, null);
            if (cpu.Text.StartsWith("слабее минимальной")) cpu.Text = cpu.Text.Replace("слабее минимальной: нужна", "слабее минимального: нужен");
            cpu.Text = cpu.Text.Replace("рекомендуемой", "рекомендуемого").Replace("минимальной", "минимального");

            PartResult ram = new PartResult(); ram.Part = "Память";
            string mine = Math.Round(pc.RamGb) + " ГБ";
            if (mn.RamGb <= 0 && rc.RamGb <= 0) { ram.Fit = Fit.NoData; ram.Text = "объём не указан"; }
            else if (rc.RamGb > 0 && pc.RamGb >= rc.RamGb * 0.93) { ram.Fit = Fit.Recommended; ram.Text = mine + ", рекомендуется " + Gb(rc.RamGb); }
            else if (mn.RamGb > 0 && pc.RamGb < mn.RamGb * 0.93) { ram.Fit = Fit.Below; ram.Text = mine + " — меньше минимума " + Gb(mn.RamGb); }
            else if (mn.RamGb > 0) { ram.Fit = Fit.Minimum; ram.Text = mine + ", минимум " + Gb(mn.RamGb) + (rc.RamGb > 0 ? ", рекомендуется " + Gb(rc.RamGb) : ""); }
            else { ram.Fit = Fit.NoData; ram.Text = mine + ", рекомендуется " + Gb(rc.RamGb); }

            v.Parts.Add(gpu); v.Parts.Add(cpu); v.Parts.Add(ram);

            if (!g.HasAny) { v.Fit = Fit.NoData; v.Title = "Нет данных"; return v; }
            // Видеокарта решает FPS: без неё честного вывода нет
            bool anyBelow = gpu.Fit == Fit.Below || cpu.Fit == Fit.Below || ram.Fit == Fit.Below;
            if (anyBelow) { v.Fit = Fit.Below; v.Title = "Ниже минимальных"; return v; }
            if (gpu.Fit == Fit.NoData) { v.Fit = Fit.NoData; v.Title = "Нет данных"; return v; }
            Fit worst = Fit.Recommended;
            foreach (PartResult p in v.Parts) if (p.Fit != Fit.NoData && p.Fit < worst) worst = p.Fit;
            v.Fit = worst;
            v.Title = worst == Fit.Recommended ? "Рекомендуемые" : "Минимальные";
            return v;
        }

        static string Gb(double v) { return (v >= 1 ? Math.Round(v, v < 4 ? 1 : 0).ToString(CultureInfo.InvariantCulture) : Math.Round(v * 1024).ToString(CultureInfo.InvariantCulture) + " М") + (v >= 1 ? " ГБ" : "Б"); }
    }
}
