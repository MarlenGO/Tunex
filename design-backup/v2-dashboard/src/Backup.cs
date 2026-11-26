using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Tunex
{
    // ---------------------------------------------------------------- Бэкапы: снимок всех настроек Tunex в один момент
    class BackupItem
    {
        public string Id, Title, Snap;
        public bool On;
    }

    class BackupFile
    {
        public string Time, Kind, Label;    // Kind: original — как было до Tunex, manual — вручную, auto — автоматически
        public List<BackupItem> Items = new List<BackupItem>();
        [ScriptIgnore] public string Path;
        [ScriptIgnore] public DateTime When { get { return DateTime.Parse(Time, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); } }
        [ScriptIgnore] public int OnCount { get { int n = 0; foreach (BackupItem i in Items) if (i.On) n++; return n; } }
    }

    static class Backups
    {
        public const int Keep = 15;
        static readonly object gate = new object();

        static string Dir { get { return System.IO.Path.Combine(AppState.Dir, "backups"); } }
        static string OriginalPath { get { return System.IO.Path.Combine(Dir, "original.json"); } }

        static JavaScriptSerializer Json() { JavaScriptSerializer j = new JavaScriptSerializer(); j.MaxJsonLength = int.MaxValue; return j; }

        // Все настройки, которые входят в бэкап: каталог, схема питания, настройки добавленных игр. Экран (яркость, гамма, частота) — нет.
        public static List<Tweak> Tracked()
        {
            List<Tweak> list = new List<Tweak>();
            foreach (KeyValuePair<string, Tweak> kv in Catalog.All) list.Add(kv.Value);
            list.Add(Catalog.PowerPlan);
            if (App.Manager != null)
                foreach (string exe in App.Manager.State.Games)
                    foreach (string kind in new string[] { "game.gpu", "game.prio", "game.fso" })
                    {
                        Tweak t = Catalog.GameTweak(kind, exe);
                        if (t != null) { t.Title = t.Title + " · " + System.IO.Path.GetFileNameWithoutExtension(exe); list.Add(t); }
                    }
            return list;
        }

        static bool Usable(Tweak t) { try { return t.IsAvailable(); } catch { return false; } }
        static bool SafeOn(Tweak t) { try { return t.ReadEnabled(); } catch { return false; } }

        // Текущее состояние: id → снимок
        public static Dictionary<string, string> Current()
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            foreach (Tweak t in Tracked())
            {
                if (!Usable(t)) continue;
                try { d[t.Id] = t.Capture(); } catch { }
            }
            return d;
        }

        public static BackupFile Make(string kind, string label)
        {
            BackupFile b = new BackupFile();
            b.Time = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            b.Kind = kind; b.Label = label;
            foreach (Tweak t in Tracked())
            {
                if (!Usable(t)) continue;
                BackupItem it = new BackupItem(); it.Id = t.Id; it.Title = t.Title;
                OriginalSnap o;
                // «Как было до Tunex»: для уже изменённых настроек берём исходное значение, а не текущее
                if (kind == "original" && App.Manager != null && App.Manager.State.Originals.TryGetValue(t.Id, out o))
                { it.Snap = o.Snapshot; it.On = o.WasOn; }
                else
                {
                    try { it.Snap = t.Capture(); } catch { continue; }
                    it.On = SafeOn(t);
                }
                b.Items.Add(it);
            }
            return b;
        }

        public static BackupFile Save(BackupFile b)
        {
            lock (gate)
            {
                Directory.CreateDirectory(Dir);
                string p = b.Kind == "original" ? OriginalPath
                    : System.IO.Path.Combine(Dir, b.When.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + b.Kind + ".json");
                File.WriteAllText(p, Json().Serialize(b), Encoding.UTF8);
                b.Path = p;
                Prune();
                return b;
            }
        }

        public static BackupFile Create(string kind, string label) { return Save(Make(kind, label)); }

        static BackupFile Load(string p)
        {
            try
            {
                BackupFile b = Json().Deserialize<BackupFile>(File.ReadAllText(p, Encoding.UTF8));
                if (b == null || b.Time == null) return null;
                if (b.Items == null) b.Items = new List<BackupItem>();
                b.Path = p;
                return b;
            }
            catch { return null; }
        }

        public static BackupFile Original() { return File.Exists(OriginalPath) ? Load(OriginalPath) : null; }

        // Сохранённые бэкапы, новые сверху (без «как было до Tunex»)
        public static List<BackupFile> List()
        {
            List<BackupFile> list = new List<BackupFile>();
            if (!Directory.Exists(Dir)) return list;
            foreach (string f in Directory.GetFiles(Dir, "*.json"))
            {
                if (string.Equals(f, OriginalPath, StringComparison.OrdinalIgnoreCase)) continue;
                BackupFile b = Load(f);
                if (b != null) list.Add(b);
            }
            list.Sort(delegate(BackupFile a, BackupFile c) { return c.When.CompareTo(a.When); });
            return list;
        }

        public static BackupFile Latest()
        {
            List<BackupFile> l = List();
            return l.Count > 0 ? l[0] : Original();
        }

        static void Prune()
        {
            List<BackupFile> l = List();
            for (int i = Keep; i < l.Count; i++) try { File.Delete(l[i].Path); } catch { }
        }

        public static void Delete(BackupFile b) { lock (gate) { try { File.Delete(b.Path); } catch { } } }

        // При первом запуске: запомнить «как было до Tunex»
        public static void EnsureOriginal()
        {
            lock (gate) { if (File.Exists(OriginalPath)) return; }
            Save(Make("original", "Как было до Tunex"));
        }

        // Раз в день, перед первым изменением
        public static void AutoDaily()
        {
            foreach (BackupFile b in List()) if (b.When.Date == DateTime.Today) return;
            Create("auto", "Автоматически перед изменениями");
        }

        // Что изменится, если восстановить: настройки, у которых сейчас другое значение
        public static List<BackupItem> Diff(BackupFile b, Dictionary<string, string> current)
        {
            List<BackupItem> d = new List<BackupItem>();
            foreach (BackupItem it in b.Items)
            {
                string cur;
                if (!current.TryGetValue(it.Id, out cur)) continue;   // настройка сейчас недоступна
                if (cur != it.Snap) d.Add(it);
            }
            return d;
        }

        // Восстановление: сперва бэкап «перед восстановлением», потом возврат отличающихся настроек с записью в журнал
        public static List<string> Restore(BackupFile b, out JournalEntry entry, out bool reboot)
        {
            Create("auto", "Перед восстановлением");
            List<KeyValuePair<Tweak, string>> work = new List<KeyValuePair<Tweak, string>>();
            List<string> errors = new List<string>();
            Dictionary<string, Tweak> byId = new Dictionary<string, Tweak>();
            foreach (Tweak t in Tracked()) byId[t.Id] = t;
            foreach (BackupItem it in Diff(b, Current()))
            {
                Tweak t;
                if (!byId.TryGetValue(it.Id, out t)) continue;
                if (t.NeedsAdmin && !SysInfo.IsAdmin) { errors.Add(t.Title + ": нужны права администратора"); continue; }
                work.Add(new KeyValuePair<Tweak, string>(t, it.Snap));
            }
            string title = b.Kind == "original" ? "Восстановлено: как было до Tunex" : "Восстановлен бэкап от " + Short(b.When);
            errors.AddRange(App.Manager.RestoreSnapshotsWork(work, title, out entry, out reboot));
            return errors;
        }

        // «сегодня, 14:32», «вчера, 09:10», «12 ноября, 18:05»
        public static string Short(DateTime t)
        {
            string day = t.Date == DateTime.Today ? "сегодня" : t.Date == DateTime.Today.AddDays(-1) ? "вчера" : t.ToString(t.Year == DateTime.Today.Year ? "d MMMM" : "d MMMM yyyy", new CultureInfo("ru-RU"));
            return day + ", " + t.ToString("HH:mm");
        }

        public static string KindName(BackupFile b)
        {
            if (b.Kind == "manual") return "Вручную";
            if (b.Kind == "original") return "До Tunex";
            return b.Label == "Перед восстановлением" ? "Перед восстановлением" : "Автоматически";
        }
    }
}
