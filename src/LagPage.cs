using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Tunex
{
    // ---------------------------------------------------------------- Раздел «Разбор лагов»
    class LagPage : Page
    {
        readonly SectionLabel lastLabel;
        readonly LagChart chart;
        readonly Card causes;
        readonly TableView history;
        readonly FlatButton now;
        readonly List<LagMoment> shown = new List<LagMoment>();
        LagMoment selected;
        bool busy;

        public LagPage()
        {
            now = new FlatButton("Разобрать сейчас", false, Icons.Lag);
            now.Click += delegate { CaptureNow(); };
            Add(new PageHeader(Icons.Lag, "Разбор лагов", "Игра дёрнулась — Tunex объяснит, что в этот момент делал компьютер.", now));

            Card intro = Add(new Card());
            SettingRow r = intro.Add(new SettingRow("Сразу после лага нажмите Ctrl + Shift + F10",
                "Tunex всё время помнит последние 2 минуты: процессор, диски, память, температуру и какие программы проснулись. " +
                "Клавиши работают прямо в игре — прозвучит короткий сигнал, а разбор появится здесь. " +
                (MainForm.LagHotkeyOk ? "" : "Сейчас клавиши заняты другой программой — нажимайте «Разобрать сейчас», как только свернёте игру."), null));
            r.Glyph = Icons.Info; r.GlyphColor = Theme.Accent;

            lastLabel = Add(new SectionLabel("Разбор"));
            chart = Add(new LagChart());
            causes = Add(new Card());

            Add(new SectionLabel("Все разборы"));
            history = Add(new TableView());
            history.Columns.Add(new TableColumn("Когда", 1.5f, false));
            history.Columns.Add(new TableColumn("Игра", 1.2f, false));
            history.Columns.Add(new TableColumn("Что было", 3.8f, false));
            history.Clickable = true;
            history.RowClick += delegate(int i) { if (i >= 0 && i < shown.Count) { selected = shown[i]; ShowMoment(); } };

            LagHistory.Added += OnAdded;
            Disposed += delegate { LagHistory.Added -= OnAdded; };

            List<LagMoment> all = LagHistory.All;
            selected = all.Count > 0 ? all[all.Count - 1] : null;
            // Для проверки интерфейса: --lagdemo показывает выдуманный разбор (не сохраняется)
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--lagdemo") >= 0) selected = SelfTest.LagDemo();
            ShowMoment();
        }

        void OnAdded(LagMoment m) { if (IsDisposed) return; selected = m; ShowMoment(); }

        void CaptureNow()
        {
            if (busy) return;
            busy = true; now.Enabled = false;
            LagHistory.Capture(App.Form.CurrentGame(), delegate(LagMoment m)
            {
                busy = false; now.Enabled = true;
                if (m == null) App.Toast("Tunex только что запустился и ещё ничего не записал. Подождите несколько секунд.", Theme.Orange);
            });
        }

        void ShowMoment()
        {
            SuspendLayout();
            chart.Moment = selected;
            chart.Visible = lastLabel.Visible = selected != null;
            chart.Invalidate();
            causes.SuspendLayout();
            causes.Clear();
            if (selected == null)
            {
                SettingRow e = causes.Add(new SettingRow("Разборов пока нет",
                    "Поиграйте и, если игра дёрнется, нажмите Ctrl + Shift + F10. Или нажмите «Разобрать сейчас», чтобы посмотреть, чем компьютер занят последние 2 минуты.", null));
                e.Glyph = Icons.Lag; e.GlyphColor = Theme.Text3;
            }
            else
            {
                for (int i = 0; i < selected.Causes.Count; i++)
                {
                    LagCause c = selected.Causes[i];
                    FlatButton go = null;
                    if (c.Action != null)
                    {
                        string key = c.Action;
                        go = new FlatButton(key == "prep" ? "Открыть «Перед игрой»" : key == "pc" ? "Открыть «Оценку ПК»" : "Открыть", false, null);
                        go.Click += delegate { App.Form.Navigate(key); };
                    }
                    SettingRow row = causes.Add(new SettingRow(c.Title, c.Text + (string.IsNullOrEmpty(c.Fix) ? "" : "\nЧто сделать: " + c.Fix), go));
                    Color col = Level(c);
                    row.Glyph = c.Score <= 0 ? Icons.Check : i == 0 ? Icons.Warning : Icons.Info;
                    row.GlyphColor = col;
                    if (selected.Causes.Count > 1) row.Badges.Add(i == 0 ? new Badge("Главная причина", col) : new Badge("Тоже могло мешать", Theme.Text2));
                }
                int secs = selected.Cpu != null ? selected.Cpu.Length : 0;
                causes.Footer = selected.Demo ? "Это пример разбора для проверки интерфейса." :
                    secs < LagAnalyzer.Window ? "Tunex успел записать только " + secs + " с — он был открыт совсем недавно. Держите Tunex открытым во время игры, тогда запись будет полной." :
                    "Tunex смотрит на последние 40 секунд перед нажатием: лаг обычно был именно там. Если причин несколько, первой стоит самая вероятная.";
            }
            causes.ResumeLayout();
            causes.Relayout();

            List<LagMoment> all = LagHistory.All;
            shown.Clear();
            history.ClearRows();
            for (int i = all.Count - 1; i >= 0; i--)
            {
                LagMoment m = all[i];
                shown.Add(m);
                LagCause c = m.Main;
                int row = history.AddRow(new TableCell[] {
                    TableCell.Of(When(m), Theme.Text2),
                    new TableCell(string.IsNullOrEmpty(m.Game) ? "—" : m.Game) { Bold = true },
                    TableCell.Of(c != null ? c.Title : "—", c != null ? Level(c) : Theme.Text2) }, false);
                if (m == selected) history.Highlight = row;
            }
            history.Footer = all.Count == 0 ? "Разборов пока нет." : "Нажмите на строку, чтобы открыть разбор. Хранятся последние " + LagHistory.Keep + ".";
            history.Done();
            ResumeLayout();
            PerformLayout();
        }

        public static Color Level(LagCause c) { return c.Score <= 0 ? Theme.Green : c.Score >= 50 ? Theme.Red : Theme.Orange; }

        public static string When(LagMoment m) { try { return Backups.Short(m.When); } catch { return "—"; } }
    }

    // ---------------------------------------------------------------- График: процессор, диск, память и видеокарта за 2 минуты
    class LagChart : BufferedControl
    {
        public LagMoment Moment;

        public LagChart() { Height = Theme.S(270); }

        static int P { get { return Theme.S(22); } }

        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            RectangleF card = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.PaintCard(g, card, Theme.Sf(14));
            LagMoment m = Moment;
            if (m == null) return;

            int x0 = P, w = Width - P * 2;
            LagCause main = m.Main;
            Color lc = main != null ? LagPage.Level(main) : Theme.Text2;
            // Заголовок: когда и в какой игре
            Theme.DrawText(g, "Что делал компьютер", Theme.FontCellTitle, Theme.Text, new Rectangle(x0, P - Theme.S(4), w, Theme.S(24)), TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
            string sub = LagPage.When(m) + (string.IsNullOrEmpty(m.Game) ? "" : " · " + m.Game) + " · последние 2 минуты до нажатия, подсвечены 40 секунд, где искали причину";
            Theme.DrawText(g, sub, Theme.FontSmall, Theme.Text2, new Rectangle(x0, P + Theme.S(20), w, Theme.S(18)), TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);

            // Подписи линий
            int ly = P + Theme.S(50);
            int lx = x0;
            lx = Legend(g, lx, ly, Theme.Accent, "Процессор");
            lx = Legend(g, lx, ly, Theme.Orange, "Диск");
            lx = Legend(g, lx, ly, Theme.Purple, "Память");
            if (Has(m.Gpu)) Legend(g, lx, ly, Theme.Green, "Видеокарта");

            int lw = Theme.S(34);
            Rectangle plot = new Rectangle(x0 + lw, ly + Theme.S(28), w - lw, Height - (ly + Theme.S(28)) - P - Theme.S(20));
            if (plot.Width < 20 || plot.Height < 20) return;

            // Окно разбора — последние 40 секунд
            float span = LagRecorder.Seconds - 1;
            float wx = X(plot, LagRecorder.Seconds - LagAnalyzer.Window, span);
            using (SolidBrush b = new SolidBrush(Theme.Alpha(Theme.Accent, Theme.Dark ? 22 : 14))) g.FillRectangle(b, wx, plot.Y, plot.Right - wx, plot.Height);

            using (Pen grid = new Pen(Theme.Border))
                for (int i = 0; i <= 2; i++)
                {
                    int yy = plot.Bottom - (int)(plot.Height * i / 2f);
                    g.DrawLine(grid, plot.X, yy, plot.Right, yy);
                    Theme.DrawText(g, (i * 50) + "%", Theme.FontSmall, Theme.Text3, new Rectangle(x0, yy - Theme.S(9), lw - Theme.S(8), Theme.S(18)), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                }
            string[] xl = { "2 мин назад", "1 мин назад", "нажатие" };
            for (int i = 0; i < 3; i++)
            {
                int xx = plot.X + plot.Width * i / 2;
                Size s = Theme.Measure(xl[i], Theme.FontSmall);
                int left = i == 0 ? xx : i == 2 ? xx - s.Width : xx - s.Width / 2;
                Theme.DrawText(g, xl[i], Theme.FontSmall, Theme.Text3, new Rectangle(left, plot.Bottom + Theme.S(4), s.Width + 2, Theme.S(18)), TextFormatFlags.VerticalCenter);
            }

            Line(g, plot, m.Ram, Theme.Purple, span);
            if (Has(m.Gpu)) Line(g, plot, m.Gpu, Theme.Green, span);
            Line(g, plot, m.Disk, Theme.Orange, span);
            Line(g, plot, m.Cpu, Theme.Accent, span);

            // Где было сильнее всего
            if (main != null && main.Score > 0 && main.At >= 0 && m.Cpu != null && main.At < m.Cpu.Length)
            {
                float ax = X(plot, LagRecorder.Seconds - m.Cpu.Length + main.At, span);
                using (Pen p = new Pen(Theme.Alpha(lc, 200), Theme.Sf(1.5f))) { p.DashStyle = DashStyle.Dash; g.DrawLine(p, ax, plot.Y, ax, plot.Bottom); }
                string t = "здесь";
                Size s = Theme.Measure(t, Theme.FontSmallBold);
                Rectangle br = new Rectangle((int)ax - s.Width / 2 - Theme.S(8), plot.Y - Theme.S(10), s.Width + Theme.S(16), s.Height + Theme.S(4));
                if (br.Right > plot.Right) br.X = plot.Right - br.Width;
                Theme.FillRound(g, lc, br, br.Height / 2f);
                Theme.DrawText(g, t, Theme.FontSmallBold, Color.White, br, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        static bool Has(float[] s) { if (s == null) return false; foreach (float v in s) if (v >= 0) return true; return false; }

        static float X(Rectangle plot, float i, float span) { return plot.X + plot.Width * Math.Max(0, i) / span; }

        static int Legend(Graphics g, int x, int y, Color c, string text)
        {
            Size s = Theme.Measure(text, Theme.FontSmall);
            using (Pen p = new Pen(c, Theme.Sf(2.5f))) g.DrawLine(p, x, y + Theme.S(9), x + Theme.S(14), y + Theme.S(9));
            Theme.DrawText(g, text, Theme.FontSmall, Theme.Text2, new Rectangle(x + Theme.S(20), y, s.Width + 2, Theme.S(18)), TextFormatFlags.VerticalCenter);
            return x + Theme.S(20) + s.Width + Theme.S(18);
        }

        // Линия по секундам, правый край — момент нажатия; пропуски (-1) разрывают линию
        static void Line(Graphics g, Rectangle plot, float[] s, Color c, float span)
        {
            if (s == null || s.Length < 2) return;
            int off = LagRecorder.Seconds - s.Length;
            List<PointF> run = new List<PointF>();
            using (Pen pen = new Pen(c, Theme.Sf(1.75f)))
            {
                pen.LineJoin = LineJoin.Round;
                for (int i = 0; i <= s.Length; i++)
                {
                    if (i < s.Length && s[i] >= 0)
                    {
                        run.Add(new PointF(X(plot, off + i, span), plot.Bottom - plot.Height * Math.Min(1f, s[i] / 100f)));
                        continue;
                    }
                    if (run.Count >= 2) g.DrawLines(pen, run.ToArray());
                    run.Clear();
                }
            }
        }
    }
}
