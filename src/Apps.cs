using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Tunex
{
    // ---------------------------------------------------------------- Discord: аппаратное ускорение
    // Discord хранит этот переключатель в %APPDATA%\discord\settings.json (ключ enableHardwareAcceleration).
    // Discord читает файл при запуске и перезаписывает его при выходе, поэтому открытый Discord закрываем и запускаем снова.
    class DiscordHwTweak : Tweak
    {
        const string Key = "enableHardwareAcceleration";
        static readonly string[] Flavors = { "discord", "discordptb", "discordcanary" };
        static readonly string[] Processes = { "Discord", "DiscordPTB", "DiscordCanary" };

        public static List<string> Files()
        {
            List<string> l = new List<string>();
            string roaming = TestRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            foreach (string f in Flavors)
            {
                string p = Path.Combine(Path.Combine(roaming, f), "settings.json");
                if (File.Exists(p)) l.Add(p);
            }
            return l;
        }

        // Для самопроверки: папка-подмена вместо %APPDATA%, Discord при этом не закрывается
        static string TestRoot { get { return Environment.GetEnvironmentVariable("TUNEX_DISCORD_DIR"); } }

        public static bool Installed { get { return Files().Count > 0; } }

        public override bool IsAvailable()
        {
            if (!Installed) { UnavailableReason = "Discord не установлен"; return false; }
            return base.IsAvailable();
        }

        static Dictionary<string, object> Load(string file)
        {
            string text = File.ReadAllText(file, Encoding.UTF8);
            Dictionary<string, object> d = string.IsNullOrEmpty(text.Trim()) ? null : new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(text);
            return d ?? new Dictionary<string, object>();
        }

        // Значение ключа: "true", "false" или "none" (ключа нет — Discord считает ускорение включённым)
        static string ReadValue(string file)
        {
            object v;
            Dictionary<string, object> d = Load(file);
            if (!d.TryGetValue(Key, out v) || !(v is bool)) return "none";
            return (bool)v ? "true" : "false";
        }

        // «Включено» = ускорение выключено во всех установленных Discord
        public override bool ReadEnabled()
        {
            List<string> files = Files();
            if (files.Count == 0) return false;
            foreach (string f in files) if (ReadValue(f) != "false") return false;
            return true;
        }

        public override string Capture()
        {
            StringBuilder sb = new StringBuilder();
            foreach (string f in Files()) sb.Append(f).Append('|').Append(ReadValue(f)).Append('\n');
            return sb.ToString();
        }

        public override void Apply(bool on)
        {
            Dictionary<string, string> want = new Dictionary<string, string>();
            foreach (string f in Files()) want[f] = on ? "false" : "none";
            Write(want);
        }

        public override void Restore(string snapshot)
        {
            Dictionary<string, string> want = new Dictionary<string, string>();
            foreach (string line in snapshot.Split('\n'))
            {
                int i = line.LastIndexOf('|');
                if (i > 0 && File.Exists(line.Substring(0, i))) want[line.Substring(0, i)] = line.Substring(i + 1);
            }
            Write(want);
        }

        static void Write(Dictionary<string, string> want)
        {
            // Ничего не меняется — Discord не трогаем
            List<string> changed = new List<string>();
            foreach (KeyValuePair<string, string> kv in want) if (ReadValue(kv.Key) != kv.Value) changed.Add(kv.Key);
            if (changed.Count == 0) return;

            List<string> wasRunning = TestRoot != null ? new List<string>() : StopDiscord();
            try
            {
                foreach (string f in changed)
                {
                    Dictionary<string, object> d = Load(f);
                    string v = want[f];
                    if (v == "none") d.Remove(Key); else d[Key] = v == "true";
                    string backup = f + ".tunex-backup";
                    if (!File.Exists(backup)) File.Copy(f, backup);
                    string tmp = f + ".tmp";
                    File.WriteAllText(tmp, new JavaScriptSerializer().Serialize(d), new UTF8Encoding(false));
                    File.Copy(tmp, f, true);
                    File.Delete(tmp);
                    if (ReadValue(f) != v) throw new IOException("Discord не принял изменение (файл " + f + ")");
                }
            }
            finally { StartDiscord(wasRunning); }
        }

        // Закрыть открытые Discord и вернуть, какие были запущены
        static List<string> StopDiscord()
        {
            List<string> running = new List<string>();
            foreach (string name in Processes)
            {
                Process[] ps = Process.GetProcessesByName(name);
                if (ps.Length == 0) continue;
                running.Add(name);
                foreach (Process p in ps) { try { p.Kill(); } catch { } }
                foreach (Process p in ps) { try { p.WaitForExit(5000); } catch { } p.Dispose(); }
            }
            if (running.Count > 0) Thread.Sleep(500);
            return running;
        }

        static void StartDiscord(List<string> names)
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (string name in names)
            {
                string folder = Path.Combine(local, name);
                string update = Path.Combine(folder, "Update.exe");
                try { if (File.Exists(update)) Process.Start(update, "--processStart " + name + ".exe"); } catch { }
            }
        }

        public static void Open()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string update = Path.Combine(Path.Combine(local, "Discord"), "Update.exe");
            if (File.Exists(update)) Process.Start(update, "--processStart Discord.exe");
            else Process.Start("discord://");
        }
    }

    // ---------------------------------------------------------------- Оверлей NVIDIA (NVIDIA App или GeForce Experience)
    // Через оверлей работают запись ShadowPlay (мгновенный повтор), игровой фильтр и фото-режим.
    // NVIDIA не даёт внешнего способа его выключить, поэтому показываем состояние и открываем приложение.
    static class NvidiaOverlay
    {
        static readonly string[] AppPaths =
        {
            @"NVIDIA Corporation\NVIDIA app\CEF\NVIDIA app.exe",
            @"NVIDIA Corporation\NVIDIA GeForce Experience\NVIDIA GeForce Experience.exe"
        };

        public static string AppPath()
        {
            string pf = Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            foreach (string rel in AppPaths)
            {
                string p = Path.Combine(pf, rel);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        public static bool IsNewApp(string path) { return path != null && path.IndexOf("NVIDIA app", StringComparison.OrdinalIgnoreCase) >= 0; }

        // Процесс оверлея запущен — значит оверлей (и запись) включены
        public static bool Running()
        {
            foreach (string n in new string[] { "NVIDIA Overlay", "NVIDIA Share" })
            {
                Process[] ps = Process.GetProcessesByName(n);
                bool any = ps.Length > 0;
                foreach (Process p in ps) p.Dispose();
                if (any) return true;
            }
            return false;
        }
    }
}
