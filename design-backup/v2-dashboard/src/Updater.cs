using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Tunex
{
    // ---------------------------------------------------------------- Автообновление из GitHub Releases
    // Как выпустить версию: поднять Theme.Version (src\Core.cs) и Version (tools\Setup.cs), собрать build.cmd,
    // на GitHub создать Release с тегом «v<версия>» и приложить файл bin\Tunex.exe под именем Tunex.exe.
    class ReleaseInfo
    {
        public string Tag, Notes, AssetUrl, Page;
        public Version Version;
        public long AssetSize;
    }

    static class Updater
    {
        public const string Repo = "MarlenGO/Tunex";   // «имя/репозиторий» на GitHub
        public const string AssetName = "Tunex.exe";
        const double CheckEveryHours = 6;

        public static bool Configured { get { return !Repo.StartsWith("OWNER/"); } }
        public static string ReleasesPage { get { return "https://github.com/" + Repo + "/releases"; } }
        public static Version Current { get { return Parse(Theme.Version); } }
        static string ExePath { get { return Application.ExecutablePath; } }

        // «v0.2», «0.2.1», «Tunex 1.0» → версия; null — не похоже на номер версии
        public static Version Parse(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            Match m = Regex.Match(tag, "(\\d+)(?:\\.(\\d+))?(?:\\.(\\d+))?");
            if (!m.Success) return null;
            int a = int.Parse(m.Groups[1].Value), b = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0, c = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
            return new Version(a, b, c);
        }

        static WebClientEx Client()
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }   // TLS 1.2
            WebClientEx w = new WebClientEx();
            w.Headers[HttpRequestHeader.UserAgent] = "Tunex/" + Theme.Version;
            w.Encoding = Encoding.UTF8;
            return w;
        }

        class WebClientEx : WebClient
        {
            protected override WebRequest GetWebRequest(Uri address)
            {
                WebRequest r = base.GetWebRequest(address);
                r.Timeout = 30000;
                return r;
            }
        }

        public static ReleaseInfo ParseRelease(string json)
        {
            Dictionary<string, object> d = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (d == null) return null;
            ReleaseInfo r = new ReleaseInfo();
            object o;
            r.Tag = d.TryGetValue("tag_name", out o) ? Convert.ToString(o) : null;
            r.Notes = d.TryGetValue("body", out o) ? Convert.ToString(o) : null;
            r.Page = d.TryGetValue("html_url", out o) ? Convert.ToString(o) : null;
            r.Version = Parse(r.Tag);
            if (d.TryGetValue("assets", out o) && o is IEnumerable)
                foreach (object a in (IEnumerable)o)
                {
                    Dictionary<string, object> asset = a as Dictionary<string, object>;
                    if (asset == null || !string.Equals(Convert.ToString(asset["name"]), AssetName, StringComparison.OrdinalIgnoreCase)) continue;
                    r.AssetUrl = Convert.ToString(asset["browser_download_url"]);
                    r.AssetSize = Convert.ToInt64(asset["size"], CultureInfo.InvariantCulture);
                }
            return r;
        }

        public static ReleaseInfo Latest()
        {
            using (WebClientEx w = Client())
            {
                w.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                return ParseRelease(w.DownloadString("https://api.github.com/repos/" + Repo + "/releases/latest"));
            }
        }

        // Скачать и подменить файл программы. Запущенный exe Windows разрешает переименовать —
        // старый уходит в «Tunex.exe.old» (удаляется при следующем запуске), новый встаёт на его место.
        public static string Install(ReleaseInfo r) { return Install(r, ExePath); }

        public static string Install(ReleaseInfo r, string exe)
        {
            string fresh = exe + ".new", old = exe + ".old";
            try
            {
                if (File.Exists(fresh)) File.Delete(fresh);
                using (WebClientEx w = Client()) w.DownloadFile(r.AssetUrl, fresh);
                FileInfo fi = new FileInfo(fresh);
                byte[] head = new byte[2];
                using (FileStream fs = File.OpenRead(fresh)) fs.Read(head, 0, 2);
                if (head[0] != 'M' || head[1] != 'Z' || (r.AssetSize > 0 && fi.Length != r.AssetSize))
                {
                    File.Delete(fresh);
                    return "скачанный файл повреждён — попробую в следующий раз";
                }
                if (File.Exists(old)) File.Delete(old);
                File.Move(exe, old);
                try { File.Move(fresh, exe); }
                catch { File.Move(old, exe); throw; }
                // Версия в «Приложениях и возможностях», если Tunex ставился установщиком
                try
                {
                    if (Reg.Get("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Tunex", "DisplayVersion") != null)
                        using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Tunex", true))
                            if (k != null) k.SetValue("DisplayVersion", r.Version.ToString(2));
                }
                catch { }
                return null;
            }
            catch (UnauthorizedAccessException) { TryDelete(fresh); return "нет прав на папку программы — запустите Tunex от администратора"; }
            catch (WebException) { TryDelete(fresh); return "не удалось скачать — проверьте интернет"; }
            catch (Exception ex) { TryDelete(fresh); return ex.Message; }
        }

        static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }

        // При запуске: убрать файл прошлой версии
        public static void CleanupOld() { TryDelete(ExePath + ".old"); TryDelete(ExePath + ".new"); }

        public static bool Due(AppState s)
        {
            DateTime last;
            return s.UpdateChecked == null || !DateTime.TryParse(s.UpdateChecked, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out last)
                || (DateTime.Now - last).TotalHours >= CheckEveryHours;
        }

        // Проверить и, если есть новая версия, поставить. Возвращает текст для пользователя; installed — поставлена новая версия.
        public static string CheckAndInstall(AppState s, out ReleaseInfo found, out bool installed)
        {
            found = null; installed = false;
            string status;
            if (!Configured) status = "адрес репозитория не задан";
            else
            {
                try
                {
                    ReleaseInfo r = Latest();
                    found = r;
                    if (r == null || r.Version == null) status = "на GitHub нет выпуска с номером версии";
                    else if (r.Version <= Current) status = "у вас последняя версия";
                    else if (r.AssetUrl == null) status = "в выпуске " + r.Tag + " нет файла " + AssetName;
                    else if (s.PendingVersion == r.Version.ToString(3)) status = "версия " + r.Version.ToString(2) + " уже скачана — откроется при следующем запуске";
                    else
                    {
                        string err = Install(r);
                        if (err == null) { installed = true; s.PendingVersion = r.Version.ToString(3); status = "скачана версия " + r.Version.ToString(2) + " — откроется при следующем запуске"; }
                        else status = "версия " + r.Version.ToString(2) + " не установлена: " + err;
                    }
                }
                catch (WebException ex)
                {
                    HttpWebResponse resp = ex.Response as HttpWebResponse;
                    status = resp != null && resp.StatusCode == HttpStatusCode.NotFound ? "на GitHub пока нет ни одного выпуска" : "нет связи с GitHub";
                }
                catch (Exception ex) { status = "ошибка проверки: " + ex.Message; }
            }
            s.UpdateChecked = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            s.UpdateStatus = status;   // сохраняет вызывающий — из потока интерфейса
            return status;
        }
    }
}
