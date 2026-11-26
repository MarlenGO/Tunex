using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace Tunex
{
    // ---------------------------------------------------------------- Раздел «Замер FPS»
    class FpsPage : Page
    {
        readonly FpsMeterCard meter;
        readonly SectionLabel resultLabel;
        readonly FpsResultView result;
        readonly TableView history;
        readonly List<FpsRun> shown = new List<FpsRun>();
        FpsRun selected;

        public FpsPage()
        {
            Add(new PageHeader(Icons.Fps, "Замер FPS", "Сколько кадров на самом деле выдаёт игра — до и после настройки.", null));
            if (!SysInfo.IsAdmin)
            {
                Card c = Add(new Card());
                FlatButton b = new FlatButton("Перезапустить", true, Icons.Shield);
                b.Click += delegate { App.Form.RestartAsAdmin(); };
                SettingRow r = c.Add(new SettingRow("Для замера нужны права администратора",
                    "Кадры игры видны только программам с правами администратора. Перезапустите Tunex — замер станет доступен.", b));
                r.Glyph = Icons.Shield; r.GlyphColor = Theme.Orange;
            }

            meter = Add(new FpsMeterCard());

            resultLabel = Add(new SectionLabel("Результат"));
            result = Add(new FpsResultView());

            Add(new SectionLabel("После игры"));
            Card sc = Add(new Card());
            ToggleSwitch sw = new ToggleSwitch();
            sw.Checked = App.Manager.State.GameSummary && PresentMon.Installed;
            sw.CheckedChanged += delegate
            {
                if (sw.Checked && !PresentMon.Installed)
                {
                    FpsMeterCard.EnsurePresentMon(delegate(bool ok)
                    {
                        if (!ok) { sw.Checked = false; return; }
                        App.Manager.State.GameSummary = true; App.Manager.State.Save();
                        App.Toast("Итог после игры включён", Theme.Green);
                    });
                    return;
                }
                App.Manager.State.GameSummary = sw.Checked; App.Manager.State.Save();
                App.Toast(sw.Checked ? "Итог после игры включён" : "Итог после игры выключен", Theme.Green);
            };
            SettingRow sr = sc.Add(new SettingRow("Итог после игры",
                "Пока идёт игра из раздела «Игры», Tunex записывает FPS. Когда вы её закроете — покажет средний FPS, просадки, рывки и температуру за всю игру. Запись почти не нагружает ПК.", sw));
            sr.Glyph = Icons.History; sr.GlyphColor = Theme.Accent;
            if (App.Manager.State.Games.Count == 0)
            {
                FlatButton go = new FlatButton("Добавить игру", false, Icons.Add);
                go.Click += delegate { App.Form.Navigate("games"); };
                SettingRow gr = sc.Add(new SettingRow("Список игр пуст", "Итог работает для игр, добавленных в разделе «Игры».", go));
                gr.Glyph = Icons.Info; gr.GlyphColor = Theme.Text2;
            }

            Add(new SectionLabel("Все замеры"));
            history = Add(new TableView());
            history.Columns.Add(new TableColumn("Игра", 2.4f, false));
            history.Columns.Add(new TableColumn("Когда", 1.6f, false));
            history.Columns.Add(new TableColumn("Вид", 1.2f, false));
            history.Columns.Add(new TableColumn("Средний FPS", 1.2f, true));
            history.Columns.Add(new TableColumn("1% низкие", 1.1f, true));
            history.Columns.Add(new TableColumn("Рывки в минуту", 1.3f, true));
            history.Clickable = true;
            history.RowClick += delegate(int i) { if (i >= 0 && i < shown.Count) { selected = shown[i]; ShowRun(); } };

            FpsMeter.Finished += OnFinished;
            Disposed += delegate { FpsMeter.Finished -= OnFinished; };

            List<FpsRun> all = App.Manager.State.FpsRuns;
            selected = FpsMeter.Last ?? (all.Count > 0 ? all[all.Count - 1] : null);
            ShowRun();
        }

        void OnFinished(FpsRun r, string err)
        {
            if (IsDisposed) return;
            if (r != null) { selected = r; ShowRun(); }
            meter.Refresh_();
        }

        void ShowRun()
        {
            result.Run = selected;
            result.Visible = resultLabel.Visible = selected != null;
            result.Relayout();
            List<FpsRun> all = App.Manager.State.FpsRuns;
            shown.Clear();
            history.ClearRows();
            for (int i = all.Count - 1; i >= 0; i--)
            {
                FpsRun r = all[i];
                shown.Add(r);
                int row = history.AddRow(new TableCell[] {
                    new TableCell(r.Game) { Bold = true },
                    TableCell.Of(Backups.Short(r.When), Theme.Text2),
                    TableCell.Of(r.Session ? "Вся игра" : "Замер", Theme.Text2),
                    new TableCell(Fps(r.Avg)) { Bold = true },
                    new TableCell(Fps(r.Low1)),
                    new TableCell(r.StuttersPerMin.ToString("0.#", CultureInfo.InvariantCulture)) }, false);
                if (r == selected) history.Highlight = row;
            }
            history.Footer = all.Count == 0 ? "Замеров пока нет." : "Нажмите на строку, чтобы открыть замер. Хранятся последние " + FpsHistory.Keep + ".";
            history.Visible = true;
            history.Invalidate();
            PerformLayout();
        }

        public static string Fps(float v) { return v < 0 ? "—" : Math.Round(v).ToString(CultureInfo.InvariantCulture); }

        public override void OnTick() { meter.Refresh_(); }
        public override void OnShow() { meter.Refresh_(); }
    }

    // ---------------------------------------------------------------- Карточка замера: шаги → отсчёт → запись
    class FpsMeterCard : BufferedControl
    {
        readonly FlatButton start, cancel, finish;
        readonly JumpBar dur;
        FpsMeter.Phase drawn = (FpsMeter.Phase)(-1);
        static bool downloading;

        static readonly string[][] Steps = {
            new string[] { "Запустите игру", "Встаньте там, где обычно много действия, или включите встроенный тест игры." },
            new string[] { "Нажмите «Начать замер»", "У вас будет " + FpsMeter.Delay + " секунд, чтобы вернуться в игру. Короткий сигнал — запись пошла." },
            new string[] { "Играйте как обычно", "Двойной сигнал — замер закончен. Результат появится ниже." } };

        public FpsMeterCard()
        {
            int d = App.Manager.State.FpsDuration;
            FpsMeter.Duration = d == 30 || d == 120 ? d : 60;

            start = new FlatButton("Начать замер", true, Icons.Play);
            start.Click += delegate { Begin(); };
            cancel = new FlatButton("Отменить", false);
            cancel.Click += delegate { FpsMeter.Cancel(); Refresh_(); };
            finish = new FlatButton("Закончить сейчас", true, Icons.Check);
            finish.Click += delegate { FpsMeter.FinishNow(); Refresh_(); };
            dur = new JumpBar();
            dur.Add("30", null, "30 с"); dur.Add("60", null, "1 мин"); dur.Add("120", null, "2 мин");
            dur.Width = Theme.S(28) * 3 + Theme.S(8) + Theme.Measure("30 с", Theme.FontBodyBold).Width + Theme.Measure("1 мин", Theme.FontBodyBold).Width + Theme.Measure("2 мин", Theme.FontBodyBold).Width;
            dur.Selected = FpsMeter.Duration.ToString(CultureInfo.InvariantCulture);
            dur.Jump += delegate(string k)
            {
                FpsMeter.Duration = int.Parse(k, CultureInfo.InvariantCulture);
                dur.Selected = k; dur.Invalidate();
                App.Manager.State.FpsDuration = FpsMeter.Duration; App.Manager.State.Save();
            };
            Controls.Add(start); Controls.Add(cancel); Controls.Add(finish); Controls.Add(dur);
            Refresh_();
        }

        void Begin()
        {
            if (!SysInfo.IsAdmin)
            {
                if (DarkDialog.Ask(App.Form, "Нужны права администратора", "Кадры игры видны только программам с правами администратора. Перезапустить Tunex?", "Перезапустить", "Отмена"))
                    App.Form.RestartAsAdmin();
                return;
            }
            EnsurePresentMon(delegate(bool ok)
            {
                if (!ok) return;
                string err = FpsMeter.Start(FpsMeter.Delay);
                if (err != null) App.Error("Замер не начался: " + err);
                Refresh_();
            });
        }

        // Скачать PresentMon (один раз, с согласия человека); done(true) — можно мерить
        public static void EnsurePresentMon(Action<bool> done)
        {
            if (PresentMon.Installed) { done(true); return; }
            if (!PresentMon.Supported) { App.Error("Замер FPS работает только в 64-битной Windows."); done(false); return; }
            if (downloading) return;
            if (!DarkDialog.Ask(App.Form, "Скачать PresentMon?",
                "Кадры считает бесплатная программа Intel PresentMon (" + PresentMon.SizeText + "). Tunex скачает её один раз с официального GitHub Intel и проверит, что файл не подменён.",
                "Скачать", "Отмена")) { done(false); return; }
            downloading = true;
            App.Toast("Скачиваю PresentMon…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err = PresentMon.Download();
                try
                {
                    App.Form.BeginInvoke(new Action(delegate
                    {
                        downloading = false;
                        if (err != null) { App.Error("PresentMon: " + err); done(false); }
                        else { App.Toast("PresentMon скачан", Theme.Green); done(true); }
                    }));
                }
                catch { }
            });
        }

        public void Refresh_()
        {
            FpsMeter.Phase ph = FpsMeter.State;
            if (ph != drawn) { drawn = ph; Relayout(); }
            Invalidate();
        }

        static int P { get { return Theme.S(22); } }

        bool Wide { get { return Width >= Theme.S(760); } }

        int StepsHeight()
        {
            if (Wide)
            {
                int cw = (Width - P * 2 - Theme.S(20) * 2) / 3, h = 0;
                foreach (string[] s in Steps) h = Math.Max(h, Theme.S(44) + Theme.Measure(s[1], Theme.FontSmall, cw).Height);
                return h;
            }
            int t = 0;
            foreach (string[] s in Steps) t += Theme.S(30) + Theme.Measure(s[1], Theme.FontSmall, Width - P * 2 - Theme.S(40)).Height + Theme.S(12);
            return t;
        }

        void Relayout()
        {
            if (Width <= 0) return;
            FpsMeter.Phase ph = FpsMeter.State;
            bool idle = ph == FpsMeter.Phase.Idle;
            start.Visible = dur.Visible = idle;
            cancel.Visible = !idle;
            finish.Visible = ph == FpsMeter.Phase.Recording;
            int h;
            if (idle)
            {
                dur.Location = new Point(Width - P - dur.Width, P - Theme.S(3));
                int y = P + Theme.S(46) + StepsHeight() + Theme.S(10);
                start.Location = new Point(P, y);
                h = y + start.Height + P;
            }
            else
            {
                int y = P + Theme.S(150);
                if (finish.Visible) { finish.Location = new Point(P, y); cancel.Location = new Point(finish.Right + Theme.S(10), y); }
                else cancel.Location = new Point(P, y);
                h = y + cancel.Height + P;
            }
            if (Height != h) Height = h;
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Relayout(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            Theme.PaintCard(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.Sf(14));
            int w = Width - P * 2;
            FpsMeter.Phase ph = FpsMeter.State;
            if (ph == FpsMeter.Phase.Idle)
            {
                Theme.DrawText(g, "Новый замер", Theme.FontH2, Theme.Text, new Rectangle(P, P, w, Theme.S(26)), TextFormatFlags.VerticalCenter);
                Theme.DrawText(g, "Сколько длится:", Theme.FontSmall, Theme.Text2, new Rectangle(P, P, dur.Left - P - Theme.S(10), Theme.S(28)), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                int y = P + Theme.S(46);
                if (Wide)
                {
                    int gap = Theme.S(20), cw = (w - gap * 2) / 3;
                    for (int i = 0; i < 3; i++) Step(g, i, P + i * (cw + gap), y, cw);
                }
                else
                    for (int i = 0; i < 3; i++)
                    {
                        int tw = w - Theme.S(40);
                        Step(g, i, P, y, w);
                        y += Theme.S(30) + Theme.Measure(Steps[i][1], Theme.FontSmall, tw).Height + Theme.S(12);
                    }
                string hint = "Уже в игре? Нажмите Ctrl + Shift + F9 — замер начнётся сразу. Ещё раз — закончится раньше.";
                int hx = start.Right + Theme.S(16);
                Theme.DrawText(g, hint, Theme.FontSmall, Theme.Text2, new Rectangle(hx, start.Top, Math.Max(Theme.S(50), Width - P - hx), start.Height),
                    TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }

            if (ph == FpsMeter.Phase.Countdown)
            {
                int left = Math.Max(0, (int)Math.Ceiling((FpsMeter.PhaseEnd - DateTime.Now).TotalSeconds));
                Theme.DrawText(g, "Переключитесь в игру", Theme.FontH2, Theme.Text, new Rectangle(P, P, w, Theme.S(26)), TextFormatFlags.VerticalCenter);
                Theme.DrawText(g, "Запись начнётся через", Theme.FontSmall, Theme.Text2, new Rectangle(P, P + Theme.S(30), w, Theme.S(20)), TextFormatFlags.VerticalCenter);
                using (Font big = new Font(Theme.FontBig.FontFamily, Theme.FontBig.Size * 2.2f, FontStyle.Bold))
                    Theme.DrawText(g, left + " с", big, Theme.Accent, new Rectangle(P, P + Theme.S(56), w, Theme.S(70)), TextFormatFlags.VerticalCenter);
                return;
            }

            // Запись
            float el = Math.Min(FpsMeter.Elapsed, FpsMeter.Duration);
            string game = FpsMeter.GameName;
            Theme.DrawText(g, "Идёт запись" + (game != null ? " · " + game : ""), Theme.FontH2, Theme.Text, new Rectangle(P, P, w, Theme.S(26)), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            Theme.DrawText(g, (int)el + " с из " + FpsMeter.Duration + " с. Играйте как обычно — двойной сигнал означает конец.", Theme.FontSmall, Theme.Text2,
                new Rectangle(P, P + Theme.S(30), w, Theme.S(20)), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            float live = FpsMeter.LiveFps;
            if (live < 0)
                Theme.DrawText(g, "Ждём кадры игры — переключитесь в неё, если ещё не там.", Theme.FontBodyBold, Theme.Text2, new Rectangle(P, P + Theme.S(56), w, Theme.S(70)), TextFormatFlags.VerticalCenter);
            else
            using (Font big = new Font(Theme.FontBig.FontFamily, Theme.FontBig.Size * 2.2f, FontStyle.Bold))
            {
                string v = FpsPage.Fps(live);
                Size vs = Theme.Measure(v, big);
                Theme.DrawText(g, v, big, Theme.Text, new Rectangle(P, P + Theme.S(56), vs.Width + 4, Theme.S(70)), TextFormatFlags.VerticalCenter);
                Theme.DrawText(g, "FPS сейчас", Theme.FontBodyBold, Theme.Text2, new Rectangle(P + vs.Width + Theme.S(10), P + Theme.S(56), Theme.S(200), Theme.S(70)), TextFormatFlags.VerticalCenter);
            }
            RectangleF bar = new RectangleF(P, P + Theme.S(132), w, Theme.S(6));
            Theme.FillRound(g, Theme.CardInner, bar, bar.Height / 2);
            float k = FpsMeter.Duration > 0 ? el / FpsMeter.Duration : 0;
            if (k > 0.01f) Theme.FillRound(g, Theme.Accent, new RectangleF(bar.X, bar.Y, bar.Width * k, bar.Height), bar.Height / 2);
        }

        void Step(Graphics g, int i, int x, int y, int cw)
        {
            int d = Theme.S(26);
            Theme.FillRound(g, Theme.AccentSoft, new RectangleF(x, y, d, d), d / 2f);
            Theme.DrawText(g, (i + 1).ToString(), Theme.FontBodyBold, Theme.AccentText, new Rectangle(x, y, d, d), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (Wide)
            {
                Theme.DrawText(g, Steps[i][0], Theme.FontBodyBold, Theme.Text, new Rectangle(x + d + Theme.S(10), y, cw - d - Theme.S(10), d), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                Theme.DrawText(g, Steps[i][1], Theme.FontSmall, Theme.Text2, new Rectangle(x, y + Theme.S(36), cw, Theme.S(80)), TextFormatFlags.WordBreak);
            }
            else
            {
                int tx = x + d + Theme.S(14);
                Theme.DrawText(g, Steps[i][0], Theme.FontBodyBold, Theme.Text, new Rectangle(tx, y, cw - d - Theme.S(14), d), TextFormatFlags.VerticalCenter);
                Theme.DrawText(g, Steps[i][1], Theme.FontSmall, Theme.Text2, new Rectangle(tx, y + Theme.S(28), cw - d - Theme.S(14), Theme.S(80)), TextFormatFlags.WordBreak);
            }
        }
    }

    // ---------------------------------------------------------------- Результат замера: числа, сравнение, вывод, график
    class FpsResultView : BufferedControl
    {
        public FpsRun Run;
        public bool Compact;          // в окне «Итог игры»: без рамки карточки
        FpsRun prev;
        int changes;

        static int P { get { return Theme.S(22); } }

        public void Relayout()
        {
            prev = Run != null ? FpsHistory.Previous(Run) : null;
            changes = prev != null ? FpsHistory.ChangesBetween(prev, Run) : 0;
            int h = MeasureHeight(Width);
            if (Height != h) Height = h;
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            int h = MeasureHeight(Width);
            if (Height != h) Height = h; else Invalidate();
        }

        int Cols { get { return Width >= Theme.S(640) ? 4 : 2; } }
        static int TileH { get { return Theme.S(92); } }

        string CompareText()
        {
            if (Run == null) return "";
            if (prev == null)
                return Run.Session ? "Это первый итог для этой игры. В следующий раз Tunex покажет, стало ли лучше."
                    : "Это первый замер этой игры. Включите настройки в «Оптимизации» и замерьте ещё раз в том же месте — Tunex покажет разницу.";
            float d = prev.Avg > 0 ? (Run.Avg - prev.Avg) / prev.Avg * 100 : 0;
            string s = "Сравнение с " + (Run.Session ? "прошлой игрой" : "прошлым замером") + " (" + Backups.Short(prev.When) + "): средний FPS " +
                FpsPage.Fps(prev.Avg) + " → " + FpsPage.Fps(Run.Avg) + " (" + (d >= 0 ? "+" : "−") + Math.Abs(Math.Round(d)).ToString(CultureInfo.InvariantCulture) + "%), 1% низкие " +
                FpsPage.Fps(prev.Low1) + " → " + FpsPage.Fps(Run.Low1) + ".";
            if (changes > 0) s += " Между ними Tunex изменил настроек: " + changes + ".";
            return s;
        }

        string SensorText()
        {
            if (Run == null) return null;
            List<string> p = new List<string>();
            if (Run.CpuTemp > 0) p.Add("процессор до " + App.Deg(Run.CpuTemp));
            if (Run.GpuTemp > 0) p.Add("видеокарта до " + App.Deg(Run.GpuTemp));
            if (Run.GpuLoad >= 0) p.Add("загрузка видеокарты в среднем " + App.Pct(Run.GpuLoad));
            if (Run.CpuLoad >= 0) p.Add("процессора " + App.Pct(Run.CpuLoad));
            return p.Count == 0 ? null : "Температура и загрузка: " + string.Join(", ", p.ToArray()) + ".";
        }

        public int MeasureHeight(int width)
        {
            if (Run == null || width <= 0) return Theme.S(10);
            int w = width - P * 2;
            int rows = 4 / (width >= Theme.S(640) ? 4 : 2);
            int y = P + Theme.S(50);
            y += rows * TileH + (rows - 1) * Theme.S(10) + Theme.S(18);
            Color c; string v = Run.Verdict(out c);
            y += Theme.Measure(v, Theme.FontBodyBold, w - Theme.S(20)).Height + Theme.S(10);
            y += Theme.Measure(CompareText(), Theme.FontSmall, w).Height + Theme.S(10);
            string st = SensorText();
            if (st != null) y += Theme.Measure(st, Theme.FontSmall, w).Height + Theme.S(10);
            foreach (string hint in Run.Hints()) y += Theme.Measure(hint, Theme.FontSmall, w - Theme.S(18)).Height + Theme.S(6);
            y += Theme.S(10) + Theme.S(150);   // график
            return y + P;
        }

        static string Duration(float sec)
        {
            int s = (int)Math.Round(sec);
            if (s < 120) return s + " с";
            int m = s / 60;
            return m < 60 ? m + " мин" : (m / 60) + " ч " + (m % 60) + " мин";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Run == null) return;
            Graphics g = e.Graphics; Theme.Smooth(g);
            if (!Compact) Theme.PaintCard(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.Sf(14));
            int w = Width - P * 2, y = P;
            Theme.DrawText(g, Run.Game, Theme.FontH2, Theme.Text, new Rectangle(P, y, w, Theme.S(26)), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            Theme.DrawText(g, (Run.Session ? "Вся игра, " : "Замер, ") + Duration(Run.Seconds) + " · " + Backups.Short(Run.When) + " · кадров: " + Run.Frames.ToString("N0", CultureInfo.GetCultureInfo("ru-RU")),
                Theme.FontSmall, Theme.Text2, new Rectangle(P, y + Theme.S(26), w, Theme.S(20)), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            y += Theme.S(50);

            // Плитки с числами
            int cols = Cols, gap = Theme.S(10), tw = (w - gap * (cols - 1)) / cols;
            Tile(g, 0, cols, tw, gap, y, "Средний FPS", FpsPage.Fps(Run.Avg), prev != null ? prev.Avg : -1, Run.Avg, true, "кадров в секунду");
            Tile(g, 1, cols, tw, gap, y, "1% низкие", FpsPage.Fps(Run.Low1), prev != null ? prev.Low1 : -1, Run.Low1, true, "тяжёлые моменты");
            Tile(g, 2, cols, tw, gap, y, "0,1% низкие", FpsPage.Fps(Run.Low01), prev != null ? prev.Low01 : -1, Run.Low01, true, "сильные просадки");
            Tile(g, 3, cols, tw, gap, y, "Рывки в минуту", Run.StuttersPerMin.ToString("0.#", CultureInfo.InvariantCulture), prev != null ? prev.StuttersPerMin : -1, Run.StuttersPerMin, false, "заметные задержки");
            int rows = 4 / cols;
            y += rows * TileH + (rows - 1) * gap + Theme.S(18);

            // Вывод
            Color vc; string verdict = Run.Verdict(out vc);
            using (SolidBrush b = new SolidBrush(vc)) g.FillEllipse(b, P, y + Theme.S(5), Theme.S(9), Theme.S(9));
            int vh = Theme.Measure(verdict, Theme.FontBodyBold, w - Theme.S(20)).Height;
            Theme.DrawText(g, verdict, Theme.FontBodyBold, Theme.Text, new Rectangle(P + Theme.S(20), y, w - Theme.S(20), vh + 2), TextFormatFlags.WordBreak);
            y += vh + Theme.S(10);

            string cmp = CompareText();
            int ch = Theme.Measure(cmp, Theme.FontSmall, w).Height;
            Theme.DrawText(g, cmp, Theme.FontSmall, Theme.Text2, new Rectangle(P, y, w, ch + 2), TextFormatFlags.WordBreak);
            y += ch + Theme.S(10);

            string st = SensorText();
            if (st != null)
            {
                int sh = Theme.Measure(st, Theme.FontSmall, w).Height;
                Theme.DrawText(g, st, Theme.FontSmall, Theme.Text2, new Rectangle(P, y, w, sh + 2), TextFormatFlags.WordBreak);
                y += sh + Theme.S(10);
            }
            foreach (string hint in Run.Hints())
            {
                int hh = Theme.Measure(hint, Theme.FontSmall, w - Theme.S(18)).Height;
                Theme.Glyph(g, Icons.Warning, Theme.FontIconTiny, Theme.Orange, new Rectangle(P, y, Theme.S(12), Theme.S(18)));
                Theme.DrawText(g, hint, Theme.FontSmall, Theme.Text, new Rectangle(P + Theme.S(18), y, w - Theme.S(18), hh + 2), TextFormatFlags.WordBreak);
                y += hh + Theme.S(6);
            }
            y += Theme.S(10);
            Chart(g, new Rectangle(P, y, w, Theme.S(150)));
        }

        void Tile(Graphics g, int i, int cols, int tw, int gap, int y0, string title, string value, float before, float now, bool higherBetter, string sub)
        {
            int c = i % cols, r = i / cols;
            Rectangle t = new Rectangle(P + c * (tw + gap), y0 + r * (TileH + gap), tw, TileH);
            Theme.FillRound(g, Theme.CardInner, new RectangleF(t.X, t.Y, t.Width, t.Height), Theme.Sf(12));
            int p = Theme.S(14);
            Theme.DrawText(g, title, Theme.FontLabel, Theme.Text2, new Rectangle(t.X + p, t.Y + Theme.S(10), t.Width - p * 2, Theme.S(18)), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            Size vs = Theme.Measure(value, Theme.FontBig);
            Theme.DrawText(g, value, Theme.FontBig, Theme.Text, new Rectangle(t.X + p, t.Y + Theme.S(30), vs.Width + 4, Theme.S(32)), TextFormatFlags.VerticalCenter);
            if (before >= 0 && Math.Abs(now - before) >= 0.5f)
            {
                bool better = higherBetter ? now > before : now < before;
                float diff = now - before;
                string dt = (diff > 0 ? "+" : "−") + (higherBetter ? Math.Abs(Math.Round(diff)).ToString(CultureInfo.InvariantCulture) : Math.Abs(diff).ToString("0.#", CultureInfo.InvariantCulture));
                Theme.DrawText(g, dt, Theme.FontSmallBold, better ? Theme.Green : Theme.Red, new Rectangle(t.X + p + vs.Width + Theme.S(8), t.Y + Theme.S(30), Theme.S(70), Theme.S(32)), TextFormatFlags.VerticalCenter);
            }
            Theme.DrawText(g, sub, Theme.FontSmall, Theme.Text3, new Rectangle(t.X + p, t.Y + Theme.S(64), t.Width - p * 2, Theme.S(18)), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        void Chart(Graphics g, Rectangle r)
        {
            float[] cur = Run.Graph;
            if (cur == null || cur.Length < 2) return;
            float[] old = prev != null ? prev.Graph : null;
            float max = 0;
            foreach (float v in cur) max = Math.Max(max, v);
            if (old != null) foreach (float v in old) max = Math.Max(max, v);
            max = (float)(Math.Ceiling(max * 1.1 / 30) * 30);
            if (max < 30) max = 30;

            int lw = Theme.S(34);
            Rectangle plot = new Rectangle(r.X + lw, r.Y + Theme.S(22), r.Width - lw, r.Height - Theme.S(22));
            // Подписи
            Theme.DrawText(g, "FPS по ходу " + (Run.Session ? "игры" : "замера"), Theme.FontLabel, Theme.Text2, new Rectangle(r.X, r.Y, r.Width / 2, Theme.S(18)), TextFormatFlags.VerticalCenter);
            int lx = r.Right;
            if (old != null) lx = Legend(g, lx, r.Y, Theme.Text3, "прошлый");
            Legend(g, lx, r.Y, Theme.Accent, "этот");
            using (Pen grid = new Pen(Theme.Border))
                for (int i = 0; i <= 3; i++)
                {
                    float v = max * i / 3;
                    int yy = plot.Bottom - (int)(plot.Height * i / 3f);
                    g.DrawLine(grid, plot.X, yy, plot.Right, yy);
                    Theme.DrawText(g, Math.Round(v).ToString(CultureInfo.InvariantCulture), Theme.FontSmall, Theme.Text3, new Rectangle(r.X, yy - Theme.S(9), lw - Theme.S(8), Theme.S(18)), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                }
            if (old != null && old.Length > 1) Line(g, plot, old, max, Theme.Alpha(Theme.Text3, 170), false);
            Line(g, plot, cur, max, Theme.Accent, true);
            // Средний FPS пунктиром
            using (Pen ap = new Pen(Theme.Alpha(Theme.Accent, 150), Theme.Sf(1)))
            {
                ap.DashStyle = DashStyle.Dash;
                float ay = plot.Bottom - plot.Height * Math.Min(1f, Run.Avg / max);
                g.DrawLine(ap, plot.X, ay, plot.Right, ay);
            }
        }

        static int Legend(Graphics g, int right, int y, Color c, string text)
        {
            Size s = Theme.Measure(text, Theme.FontSmall);
            int x = right - s.Width;
            Theme.DrawText(g, text, Theme.FontSmall, Theme.Text2, new Rectangle(x, y, s.Width + 2, Theme.S(18)), TextFormatFlags.VerticalCenter);
            using (Pen p = new Pen(c, Theme.Sf(2))) g.DrawLine(p, x - Theme.S(20), y + Theme.S(9), x - Theme.S(6), y + Theme.S(9));
            return x - Theme.S(34);
        }

        static void Line(Graphics g, Rectangle plot, float[] s, float max, Color c, bool fill)
        {
            PointF[] pts = new PointF[s.Length];
            for (int i = 0; i < s.Length; i++)
                pts[i] = new PointF(plot.X + plot.Width * i / (float)(s.Length - 1), plot.Bottom - plot.Height * Math.Min(1f, Math.Max(0, s[i]) / max));
            if (fill)
                using (GraphicsPath area = new GraphicsPath())
                {
                    area.AddLines(pts);
                    area.AddLine(pts[pts.Length - 1], new PointF(plot.Right, plot.Bottom));
                    area.AddLine(new PointF(plot.Right, plot.Bottom), new PointF(plot.X, plot.Bottom));
                    area.CloseFigure();
                    using (LinearGradientBrush lb = new LinearGradientBrush(new Rectangle(plot.X, plot.Y - 1, plot.Width, plot.Height + 2), Theme.Alpha(c, 45), Theme.Alpha(c, 0), 90f))
                        g.FillPath(lb, area);
                }
            using (Pen pen = new Pen(c, Theme.Sf(fill ? 1.75f : 1.25f))) { pen.LineJoin = LineJoin.Round; g.DrawLines(pen, pts); }
        }
    }

    // ---------------------------------------------------------------- Окно «Итог игры» после закрытия игры
    class FpsSummaryDialog : DialogCard
    {
        readonly FpsResultView view;
        readonly FlatButton ok, more;

        public FpsSummaryDialog(FpsRun run)
        {
            Width = Theme.S(720);
            view = new FpsResultView(); view.Compact = true; view.Run = run;
            view.Location = new Point(0, Theme.S(48));
            view.Width = Width;
            Controls.Add(view);
            ok = new FlatButton("Понятно", true);
            ok.Click += delegate { App.Form.CloseModal(); };
            more = new FlatButton("Все замеры", false);
            more.Click += delegate { App.Form.CloseModal(); App.Form.Navigate("fps"); };
            Controls.Add(ok); Controls.Add(more);
            view.Relayout();
            int y = view.Bottom;
            ok.Location = new Point(Width - Pad - ok.Width, y);
            more.Location = new Point(ok.Left - Theme.S(10) - more.Width, y);
            Height = y + ok.Height + Pad;
        }

        protected override void PaintContent(Graphics g)
        {
            Theme.DrawText(g, view.Run != null && view.Run.Session ? "Итог игры" : "Итог замера", Theme.FontTitle, Theme.Text, new Rectangle(Pad, Theme.S(18), Width - Pad * 2, Theme.S(34)), TextFormatFlags.VerticalCenter);
        }
    }
}
