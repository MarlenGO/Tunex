using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Tunex
{
    // ---------------------------------------------------------------- Раздел «Перед игрой»: разгрузка, «не беспокоить», пинг
    class PlayPage : Page
    {
        readonly Card unloadCard, pingCard, loadCard;
        readonly TableView loadTable;
        LoadResult loadRes; bool loading; string loadStatus;
        readonly TweakGrid auto;
        readonly JumpBar games;
        readonly TableView pingTable;
        readonly FlatButton refresh;
        List<AppGroup> groups;
        bool scanning, closing, pinging;
        string game = "cs2";

        class PingResult { public List<PingTarget> Servers; public PingTarget Router; }
        readonly Dictionary<string, PingResult> results = new Dictionary<string, PingResult>();

        public PlayPage()
        {
            refresh = new FlatButton("Обновить список", false, Icons.Refresh);
            refresh.Click += delegate { Scan(); };
            Add(new PageHeader(Icons.Play, "Перед игрой", "Освободите компьютер и проверьте интернет — за минуту до игры.", refresh));

            Add(new SectionLabel("Разгрузка"));
            unloadCard = Add(new Card());

            auto = Add(new TweakGrid("Автоматически", "Для игр из раздела «Игры»: срабатывает, когда игра запускается. Когда игра закрыта, всё возвращается."));
            auto.AddTweak(Catalog.Resolve("session.unload"));
            auto.AddTweak(Catalog.Resolve("session.quiet"));

            Add(new SectionLabel("Пинг до игровых серверов"));
            games = Add(new JumpBar());
            foreach (string g in NetPing.Games) games.Add(g, Icons.Network, NetPing.Title(g));
            games.Selected = game;
            games.Jump += delegate(string k) { if (pinging) { games.Selected = game; games.Invalidate(); return; } game = k; games.Selected = k; games.Invalidate(); ShowPing(); };
            games.Margin = new Padding(0, 0, 0, Theme.S(12));
            pingCard = Add(new Card());
            pingTable = Add(new TableView());
            pingTable.Columns.Add(new TableColumn("Сервер", 2.4f, false));
            pingTable.Columns.Add(new TableColumn("Пинг", 1.0f, true));
            pingTable.Columns.Add(new TableColumn("Скачки", 1.0f, true));
            pingTable.Columns.Add(new TableColumn("Потери", 1.0f, true));
            pingTable.Columns.Add(new TableColumn("Оценка", 1.3f, true));

            Add(new SectionLabel("Пинг, когда интернет занят"));
            loadCard = Add(new Card());
            loadTable = Add(new TableView());
            loadTable.Columns.Add(new TableColumn("Что происходит", 2.4f, false));
            loadTable.Columns.Add(new TableColumn("Пинг", 1.0f, true));
            loadTable.Columns.Add(new TableColumn("Рост", 1.0f, true));
            loadTable.Columns.Add(new TableColumn("Потери", 1.0f, true));
            loadTable.Columns.Add(new TableColumn("Скорость", 1.3f, true));

            FillUnload();
            ShowPing();
            ShowLoad();
        }

        public override void OnShow()
        {
            auto.SyncAll();
            if (groups == null && !scanning) Scan();
            // Для проверки интерфейса: --ping запускает замер пинга сразу
            if (!autoPinged && Array.IndexOf(Environment.GetCommandLineArgs(), "--ping") >= 0) { autoPinged = true; RunPing(); }
            if (!autoLoaded && Array.IndexOf(Environment.GetCommandLineArgs(), "--load") >= 0) { autoLoaded = true; RunLoad(); }
        }
        bool autoPinged, autoLoaded;

        // ================================================================ Разгрузка
        void Scan()
        {
            if (scanning || closing) return;
            scanning = true; refresh.Enabled = false;
            FillUnload();
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<AppGroup> l = null;
                try { l = Unload.Scan(); } catch { l = new List<AppGroup>(); }
                Ui(delegate { groups = l; scanning = false; refresh.Enabled = true; FillUnload(); });
            });
        }

        void Ui(Action a) { try { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); } catch { } }

        void FillUnload()
        {
            SuspendLayout();
            unloadCard.SuspendLayout();
            unloadCard.Clear();
            List<ClosedApp> closed = App.Manager.State.Unloaded;

            if (closed.Count > 0)
            {
                FlatButton back = new FlatButton("Вернуть сейчас", false, Icons.Undo);
                back.Click += delegate { RestoreNow(); };
                List<string> names = new List<string>();
                foreach (ClosedApp c in closed) if (!names.Contains(c.Name)) names.Add(c.Name);
                SettingRow r = unloadCard.Add(new SettingRow("Закрыто на время игры: " + string.Join(", ", names.ToArray()),
                    "Программы откроются снова, когда вы закроете игру из раздела «Игры». Можно вернуть и сейчас.", back));
                r.Glyph = Icons.History; r.GlyphColor = Theme.Accent;
            }

            if (scanning || groups == null)
            {
                SettingRow w = unloadCard.Add(new SettingRow(closing ? "Закрываю программы…" : "Смотрю, что сейчас запущено…", "Пара секунд.", null));
                w.Glyph = Icons.Refresh; w.GlyphColor = Theme.Text3;
            }
            else if (groups.Count == 0)
            {
                SettingRow ok = unloadCard.Add(new SettingRow("Лишних программ не нашлось",
                    "Браузеры, торренты, облачные диски и другие тяжёлые программы сейчас не запущены. Можно играть.", null));
                ok.Glyph = Icons.Check; ok.GlyphColor = Theme.Green;
            }
            else
            {
                FlatButton go = new FlatButton(closing ? "Закрываю…" : "Закрыть выбранные", true, Icons.Broom);
                go.Enabled = !closing;
                go.Click += delegate { CloseNow(); };
                SettingRow sum = unloadCard.Add(new SettingRow("Сейчас занимают ресурсы", SummaryText(), go));
                sum.Glyph = Icons.Broom; sum.GlyphColor = Theme.Accent;
                foreach (AppGroup g in groups)
                {
                    AppGroup gg = g;
                    ToggleSwitch sw = new ToggleSwitch();
                    sw.Checked = Unload.Picked(g);
                    SettingRow r = new SettingRow(g.Name, Unload.Size(g.Memory) + " памяти · процессор " + App.Pct(g.Cpu), sw);
                    sw.CheckedChanged += delegate { Unload.SetPicked(gg, sw.Checked); sum.Description = SummaryText(); unloadCard.Relayout(); sum.Invalidate(); };
                    r.Glyph = KindGlyph(g.Kind); r.GlyphColor = Theme.Text2;
                    r.Badges.Add(new Badge(g.Kind, Theme.Text2));
                    unloadCard.Add(r);
                }
            }
            unloadCard.Footer = "Окна закрываются как крестиком. Вкладки браузер вернёт, если в его настройках включено «Продолжать с того же места», или нажмите Ctrl + Shift + T. " +
                "Если программа спрашивает о сохранении, Tunex её не трогает. Steam, клиент Riot и античиты в список не попадают — без них игры не запустятся.";
            unloadCard.ResumeLayout();
            unloadCard.Relayout();
            ResumeLayout();
        }

        string SummaryText()
        {
            long mem = 0; int n = 0;
            if (groups != null) foreach (AppGroup g in groups) if (Unload.Picked(g)) { mem += g.Memory; n++; }
            return n == 0 ? "Включите то, что закрыть перед игрой. Tunex запомнит выбор."
                : "Выбрано: " + n + " — освободится около " + Unload.Size(mem) + " памяти. Tunex запомнит выбор.";
        }

        static string KindGlyph(string kind)
        {
            switch (kind)
            {
                case "Браузер": case "Торрент": return Icons.Network;
                case "Облако": return Icons.Save;
                case "Лаунчер": return Icons.Library;
                case "Общение": case "Музыка": return Icons.Game;
                default: return Icons.System;
            }
        }

        void CloseNow()
        {
            if (closing || groups == null) return;
            List<AppGroup> picked = new List<AppGroup>();
            foreach (AppGroup g in groups) if (Unload.Picked(g)) picked.Add(g);
            if (picked.Count == 0) { App.Toast("Ничего не выбрано — включите переключатели у программ, которые закрыть.", Theme.Orange); return; }
            closing = true; refresh.Enabled = false;
            FillUnload();
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<string> failed = new List<string>();
                List<ClosedApp> done = new List<ClosedApp>();
                try { done = Unload.Close(picked, failed); } catch { }
                long freed = 0;
                foreach (AppGroup g in picked) if (!failed.Contains(g.Name)) freed += g.Memory;
                Ui(delegate
                {
                    closing = false;
                    App.Manager.State.Unloaded.AddRange(done);
                    App.Manager.State.Save();
                    string msg = done.Count > 0 || failed.Count < picked.Count ? "Закрыто. Освободилось около " + Unload.Size(freed) + " памяти." : "";
                    if (failed.Count > 0) msg += (msg != "" ? " " : "") + "Не закрылись: " + string.Join(", ", failed.ToArray()) + " — посмотрите, не спрашивают ли они о сохранении.";
                    App.Toast(msg, failed.Count > 0 ? Theme.Orange : Theme.Green);
                    groups = null; scanning = false; refresh.Enabled = true;
                    Scan();
                });
            });
        }

        void RestoreNow()
        {
            List<ClosedApp> list = new List<ClosedApp>(App.Manager.State.Unloaded);
            App.Manager.State.Unloaded.Clear();
            App.Manager.State.Save();
            FillUnload();
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<string> names = Unload.Restore(list);
                Thread.Sleep(1500);
                Ui(delegate
                {
                    App.Toast(names.Count > 0 ? "Вернул: " + string.Join(", ", names.ToArray()) + "." : "Нечего возвращать.", Theme.Green);
                    groups = null; Scan();
                });
            });
        }

        // ================================================================ Пинг
        void ShowPing()
        {
            SuspendLayout();
            pingCard.SuspendLayout();
            pingCard.Clear();
            PingResult res;
            results.TryGetValue(game, out res);

            FlatButton go = new FlatButton(pinging ? "Измеряю…" : res == null ? "Проверить пинг" : "Проверить ещё раз", false, Icons.Network);
            go.Enabled = !pinging;
            go.Click += delegate { RunPing(); };

            if (res == null || pinging)
            {
                SettingRow r = pingCard.Add(new SettingRow(pinging ? "Измеряю пинг до серверов " + NetPing.Title(game) + "…" : "Пинг до серверов " + NetPing.Title(game),
                    pinging ? "Около 5 секунд. Ищу ближайшие серверы, потом по 20 раз проверяю каждый и ваш роутер."
                            : "Tunex найдёт ближайшие серверы и измерит задержку, скачки и потери. И подскажет, кто виноват: Wi-Fi, провайдер или далёкий сервер.", go));
                r.Glyph = Icons.Network; r.GlyphColor = pinging ? Theme.Text3 : Theme.Accent;
                pingTable.Visible = false;
            }
            else
            {
                string text; Color c;
                string title = NetPing.Verdict(game, res.Servers, res.Router, out text, out c);
                SettingRow r = pingCard.Add(new SettingRow(title, text, go));
                r.Glyph = c == Theme.Green ? Icons.Check : Icons.Warning; r.GlyphColor = c;

                pingTable.ClearRows();
                List<PingTarget> rows = new List<PingTarget>();
                if (res.Router != null) rows.Add(res.Router);
                rows.AddRange(res.Servers);
                foreach (PingTarget t in rows)
                {
                    Color gc; string grade = NetPing.Grade(t, out gc);
                    pingTable.AddRow(new TableCell[] {
                        new TableCell(t.Router ? "Ваш роутер (домашняя сеть)" : t.Name) { Bold = true },
                        new TableCell(t.Ok ? NetPing.Ms(t.Avg) : "—") { Bold = true },
                        TableCell.Of(t.Ok ? NetPing.Ms(t.Jitter) : "—", Theme.Text2),
                        TableCell.Of(NetPing.Pct(t.Loss), t.Loss >= 2 ? Theme.Red : Theme.Text2),
                        TableCell.Pill(grade, gc) }, false);
                }
                pingTable.Footer = (game == "valorant"
                    ? "У Riot нет открытого списка серверов, поэтому для Valorant пинг измерен до дата-центров Amazon в тех же городах. Это близкая оценка, а не пинг в самой игре. "
                    : "Серверы Valve из открытого списка Steam, показаны ближайшие. ") +
                    "Скачки — насколько пинг меняется от раза к разу, потери — сколько ответов не пришло. Для игр важно, чтобы оба числа были около нуля.";
                pingTable.Visible = true;
                pingTable.Done();
            }
            pingCard.ResumeLayout();
            pingCard.Relayout();
            ResumeLayout();
            PerformLayout();
        }

        void RunPing()
        {
            if (pinging) return;
            pinging = true;
            string g = game;
            ShowPing();
            ThreadPool.QueueUserWorkItem(delegate
            {
                PingResult r = new PingResult();
                try { r.Servers = NetPing.Run(g, out r.Router); } catch { r.Servers = new List<PingTarget>(); }
                Ui(delegate { pinging = false; results[g] = r; if (game == g) ShowPing(); });
            });
        }
    
        // ================================================================ Пинг под нагрузкой
        void ShowLoad()
        {
            SuspendLayout();
            loadCard.SuspendLayout();
            loadCard.Clear();
            FlatButton go = new FlatButton(loading ? "Проверяю…" : loadRes == null ? "Проверить" : "Проверить ещё раз", false, Icons.Network);
            go.Enabled = !loading;
            go.Click += delegate { RunLoad(); };
            if (loadRes == null || loading)
            {
                SettingRow r = loadCard.Add(new SettingRow(loading ? loadStatus ?? "Проверяю…" : "Лагает, когда кто-то дома смотрит видео?",
                    loading ? "Около 20 секунд. Во время проверки интернет будет занят полностью."
                            : "Tunex займёт интернет на полную скорость — сначала скачиванием, потом отправкой — и в это время померит пинг. " +
                              "Так видно, справляется ли роутер, когда канал занят. Проверка скачает и отправит проверочные данные (на быстром интернете — несколько сотен МБ), на диск ничего не сохраняется.", go));
                r.Glyph = Icons.Network; r.GlyphColor = loading ? Theme.Text3 : Theme.Accent;
                loadTable.Visible = false;
            }
            else
            {
                string text; Color c;
                string title = LoadTest.Verdict(loadRes, out text, out c);
                SettingRow r = loadCard.Add(new SettingRow(title, text, go));
                r.Glyph = c == Theme.Green ? Icons.Check : Icons.Warning; r.GlyphColor = c;
                loadTable.ClearRows();
                if (loadRes.Error == null)
                {
                    float idle = loadRes.Idle.Median;
                    foreach (LoadPhase ph in new LoadPhase[] { loadRes.Idle, loadRes.Down, loadRes.Up })
                    {
                        if (ph == null) continue;
                        bool failed = ph != loadRes.Idle && ph.Mbps <= 0;
                        float grow = ph == loadRes.Idle || failed || ph.Median < 0 ? -1 : ph.Median - idle;
                        Color gc = grow < 0 ? Theme.Text2 : grow < 30 ? Theme.Green : grow < 80 ? Theme.Orange : Theme.Red;
                        loadTable.AddRow(new TableCell[] {
                            new TableCell(ph.Name) { Bold = true },
                            new TableCell(NetPing.Ms(ph.Median)) { Bold = true },
                            grow < 0 ? TableCell.Of("—", Theme.Text2) : TableCell.Pill("+" + NetPing.Ms(grow), gc),
                            TableCell.Of(NetPing.Pct(ph.Loss), ph.Loss >= 8 ? Theme.Red : ph.Loss >= 3 ? Theme.Orange : Theme.Text2),
                            TableCell.Of(ph == loadRes.Idle ? "—" : failed ? "не удалось" : LoadTest.Speed(ph.Mbps), failed ? Theme.Orange : Theme.Text2) }, false);
                    }
                    loadTable.Footer = "Пинг до " + LoadTest.Target + " (Cloudflare) — середина из всех замеров. Рост до 30 мс почти незаметен в игре, больше 80 мс — заметные лаги. " +
                        "Скорость — сколько удалось скачать и отправить за " + LoadTest.Seconds + " секунд.";
                    loadTable.Visible = true;
                    loadTable.Done();
                }
                else loadTable.Visible = false;
            }
            loadCard.ResumeLayout();
            loadCard.Relayout();
            ResumeLayout();
            PerformLayout();
        }

        void RunLoad()
        {
            if (loading) return;
            loading = true; loadStatus = null;
            ShowLoad();
            ThreadPool.QueueUserWorkItem(delegate
            {
                LoadResult r;
                try { r = LoadTest.Run(delegate(string st) { Ui(delegate { loadStatus = st; if (loading) ShowLoad(); }); }); }
                catch (Exception ex) { r = new LoadResult(); r.Error = ex.Message; }
                Ui(delegate { loading = false; loadRes = r; ShowLoad(); });
            });
        }
    }
}
