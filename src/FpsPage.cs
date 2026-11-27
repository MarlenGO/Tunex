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
            history.Done();
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
        readonly Stepper dur, delay;
        FpsMeter.Phase drawn = (FpsMeter.Phase)(-1);
        static bool downloading;

        // Длительность: каждая минута до 10, дальше через 5 минут — до 3 часов
        static int[] Minutes()
        {
            List<int> l = new List<int>();
            for (int i = 1; i <= 10; i++) l.Add(i);
            for (int i = 15; i <= 180; i += 5) l.Add(i);
            return l.ToArray();
        }

        static string[][] Steps()
        {
            return new string[][] {
                new string[] { "Запустите игру", "Встаньте там, где обычно много действия, или включите встроенный тест игры. Для целой катки поставьте её длительность." },
                new string[] { "Нажмите «Начать замер»", FpsMeter.Delay > 0
                    ? "У вас будет " + FpsMeter.Delay + " с, чтобы вернуться в игру. Короткий сигнал — запись пошла."
                    : "Запись начнётся сразу — короткий сигнал. Удобно, если игра уже открыта во втором окне." },
                new string[] { "Играйте как обычно", "Двойной сигнал — замер закончен. Закончить раньше — Ctrl + Shift + F9. Результат появится ниже." } };
        }

        public FpsMeterCard()
        {
            AppState st = App.Manager.State;
            FpsMeter.Duration = Math.Max(60, (int)Math.Round((st.FpsDuration > 0 ? st.FpsDuration : 60) / 60.0) * 60);
            FpsMeter.Delay = Math.Max(0, Math.Min(60, st.FpsDelay));

            start = new FlatButton("Начать замер", true, Icons.Play);
            start.Click += delegate { Begin(); };
            cancel = new FlatButton("Отменить", false);
            cancel.Click += delegate { FpsMeter.Cancel(); Refresh_(); };
            finish = new FlatButton("Закончить сейчас", true, Icons.Check);
            finish.Click += delegate { FpsMeter.FinishNow(); Refresh_(); };

            dur = new Stepper(Minutes(), FpsMeter.Duration / 60, delegate(int v) { return v < 60 ? v + " мин" : v % 60 == 0 ? v / 60 + " ч" : v / 60 + " ч " + v % 60 + " мин"; });
            dur.Changed += delegate(int v)
            {
                FpsMeter.Duration = v * 60;
                st.FpsDuration = FpsMeter.Duration; st.Save();
            };
            delay = new Stepper(new int[] { 0, 3, 5, 10, 15, 20, 30, 45, 60 }, FpsMeter.Delay, delegate(int v) { return v == 0 ? "сразу" : v + " с"; });
            delay.Changed += delegate(int v)
            {
                FpsMeter.Delay = v;
                st.FpsDelay = v; st.Save();
                Relayout();
            };
            Controls.Add(start); Controls.Add(cancel); Controls.Add(finish); Controls.Add(dur); Controls.Add(delay);
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
        static int Pi { get { return Theme.S(16); } }      // отступ внутри карточки шага
        static int Gap { get { return Theme.S(12); } }

        bool Wide { get { return Width >= Theme.S(760); } }

        const string DurLabel = "Длительность", DelayLabel = "Отсчёт перед стартом";

        int stepsY;

        int StepW { get { return Wide ? (Width - P * 2 - Gap * 2) / 3 : Width - P * 2; } }

        int StepH(string[] s)
        {
            int tw = StepW - Pi * 2;
            return Pi + Theme.S(26) + Theme.S(10) + Theme.Measure(s[1], Theme.FontSmall, tw).Height + Pi;
        }

        int StepsHeight()
        {
            string[][] steps = Steps();
            if (Wide) { int h = 0; foreach (string[] s in steps) h = Math.Max(h, StepH(s)); return h; }
            int t = 0;
            foreach (string[] s in steps) t += StepH(s) + Gap;
            return t - Gap;
        }

        void Relayout()
        {
            if (Width <= 0) return;
            FpsMeter.Phase ph = FpsMeter.State;
            bool idle = ph == FpsMeter.Phase.Idle;
            start.Visible = dur.Visible = delay.Visible = idle;
            cancel.Visible = !idle;
            finish.Visible = ph == FpsMeter.Phase.Recording;
            int h;
            if (idle)
            {
                // Строка выбора времени: «Длительность [− 45 мин +]   Отсчёт перед стартом [− 10 с +]»; узко — в две строки
                int sy = P + Theme.S(42);
                int l1 = Theme.Measure(DurLabel, Theme.FontSmall).Width + Theme.S(12);
                int l2 = Theme.Measure(DelayLabel, Theme.FontSmall).Width + Theme.S(12);
                dur.Location = new Point(P + l1, sy);
                int x2 = dur.Right + Theme.S(32);
                if (x2 + l2 + delay.Width <= Width - P) delay.Location = new Point(x2 + l2, sy);
                else delay.Location = new Point(P + l2, sy + dur.Height + Theme.S(10));
                stepsY = delay.Bottom + Theme.S(20);
                int y = stepsY + StepsHeight() + Theme.S(18);
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

        static string Clock(float sec)
        {
            int s = (int)sec;
            return s >= 3600 ? (s / 3600) + ":" + (s / 60 % 60).ToString("00") + ":" + (s % 60).ToString("00") : (s / 60) + ":" + (s % 60).ToString("00");
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            Theme.PaintCard(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.Sf(14));
            int w = Width - P * 2;
            FpsMeter.Phase ph = FpsMeter.State;
            if (ph == FpsMeter.Phase.Idle)
            {
                Theme.DrawText(g, "Новый замер", Theme.FontH2, Theme.Text, new Rectangle(P, P, w, Theme.S(26)), TextFormatFlags.VerticalCenter);
                int l1 = Theme.Measure(DurLabel, Theme.FontSmall).Width + 2;
                Theme.DrawText(g, DurLabel, Theme.FontSmall, Theme.Text2, new Rectangle(dur.Left - Theme.S(12) - l1, dur.Top, l1, dur.Height), TextFormatFlags.VerticalCenter);
                int l2 = Theme.Measure(DelayLabel, Theme.FontSmall).Width + 2;
                Theme.DrawText(g, DelayLabel, Theme.FontSmall, Theme.Text2, new Rectangle(delay.Left - Theme.S(12) - l2, delay.Top, l2, delay.Height), TextFormatFlags.VerticalCenter);

                string[][] steps = Steps();
                int y = stepsY;
                if (Wide)
                {
                    int sh = StepsHeight();
                    for (int i = 0; i < 3; i++) Step(g, steps, i, P + i * (StepW + Gap), y, StepW, sh);
                }
                else
                    for (int i = 0; i < 3; i++) { int sh = StepH(steps[i]); Step(g, steps, i, P, y, StepW, sh); y += sh + Gap; }

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
            Theme.DrawText(g, Clock(el) + " из " + Clock(FpsMeter.Duration) + ". Играйте как обычно — двойной сигнал означает конец.", Theme.FontSmall, Theme.Text2,
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

        // Шаг — в тихой карточке: чуть темнее фона, без рамки
        void Step(Graphics g, string[][] steps, int i, int x, int y, int cw, int ch)
        {
            RectangleF box = new RectangleF(x, y, cw, ch);
            Theme.FillRound(g, Theme.CardInner, box, Theme.Sf(12));
            int d = Theme.S(26);
            int ix = x + Pi, iy = y + Pi;
            Theme.FillRound(g, Theme.AccentSoft, new RectangleF(ix, iy, d, d), d / 2f);
            Theme.DrawText(g, (i + 1).ToString(), Theme.FontBodyBold, Theme.AccentText, new Rectangle(ix, iy, d, d), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Theme.DrawText(g, steps[i][0], Theme.FontBodyBold, Theme.Text, new Rectangle(ix + d + Theme.S(10), iy, cw - Pi * 2 - d - Theme.S(10), d), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            Theme.DrawText(g, steps[i][1], Theme.FontSmall, Theme.Text2, new Rectangle(ix, iy + d + Theme.S(10), cw - Pi * 2, ch - Pi * 2 - d - Theme.S(10) + Theme.S(4)), TextFormatFlags.WordBreak);
        }
    }

    // ---------------------------------------------------------------- Выбор числа: «−  45 мин  +» (клик, удержание, колесо мыши)
    class Stepper : BufferedControl
    {
        readonly int[] values;
        readonly Func<int, string> format;
        int index, hover = -2, held;
        readonly System.Windows.Forms.Timer repeat = new System.Windows.Forms.Timer();
        public event Action<int> Changed;

        public Stepper(int[] values, int value, Func<int, string> format)
        {
            this.values = values; this.format = format;
            index = 0;
            for (int i = 0; i < values.Length; i++) if (values[i] <= value) index = i;
            int widest = 0;
            foreach (int v in values) widest = Math.Max(widest, Theme.Measure(format(v), Theme.FontBodyBold).Width);
            Size = new Size(widest + Theme.S(34) * 2 + Theme.S(12), Theme.S(34));
            Cursor = Cursors.Hand;
            repeat.Tick += delegate { repeat.Interval = 70; Step(held); };
            Disposed += delegate { repeat.Dispose(); };
        }

        public int Value { get { return values[index]; } }

        int Zone(int x) { int b = Theme.S(34); return x < b ? -1 : x > Width - b ? 1 : 0; }

        void Step(int dir)
        {
            int n = Math.Max(0, Math.Min(values.Length - 1, index + dir));
            if (n == index) return;
            index = n; Invalidate();
            if (Changed != null) Changed(Value);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int z = Zone(e.X);
            if (z == 0) return;
            held = z; Step(z);
            repeat.Interval = 400; repeat.Start();
        }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); repeat.Stop(); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int z = Zone(e.X); if (z != hover) { hover = z; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -2; repeat.Stop(); Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Step(e.Delta > 0 ? 1 : -1); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRound(g, Theme.Field, r, r.Height / 2f);
            Theme.DrawRound(g, Theme.FieldEdge, r, r.Height / 2f, 1f);
            int b = Theme.S(34);
            for (int s = -1; s <= 1; s += 2)
            {
                bool can = s < 0 ? index > 0 : index < values.Length - 1;
                Rectangle br = s < 0 ? new Rectangle(0, 0, b, Height) : new Rectangle(Width - b, 0, b, Height);
                if (hover == s && can) Theme.FillRound(g, Theme.CardHover, new RectangleF(br.X + Theme.S(3), Theme.S(3), br.Width - Theme.S(6), Height - Theme.S(6)), (Height - Theme.S(6)) / 2f);
                Theme.DrawText(g, s < 0 ? "−" : "+", Theme.FontCellTitle, can ? Theme.Text : Theme.Text3, br, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            Theme.DrawText(g, format(Value), Theme.FontBodyBold, Theme.Text, new Rectangle(b, 0, Width - b * 2, Height), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
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
