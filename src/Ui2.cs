using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace Tunex
{
    // ---------------------------------------------------------------- Состояние настройки для метки-полоски
    class TweakState
    {
        public bool Available, On, AdminBlocked, ByTunex;
        public string Note;
        public Color Color;
        // Понятная подпись: применено ли, кем, и стоит ли применять
        public string Status;

        public static TweakState Of(Tweak t)
        {
            TweakState s = new TweakState();
            try { s.Available = t.IsAvailable(); } catch { s.Available = false; }
            if (s.Available) { try { s.On = t.ReadEnabled(); } catch { s.Available = false; t.UnavailableReason = "Нет доступа"; } }
            s.AdminBlocked = s.Available && t.NeedsAdmin && !SysInfo.IsAdmin;
            if (!s.Available) s.Note = t.UnavailableReason ?? "Недоступно на этом ПК";
            else if (s.AdminBlocked) s.Note = "Нужны права администратора";
            if (!s.Available) s.Color = Theme.ToggleOff;
            else if (s.On) s.Color = Theme.Green;
            else if (t.Rec == Recommend.All) s.Color = Theme.Red;
            else if (t.Rec == Recommend.Some) s.Color = Theme.Orange;
            else s.Color = Theme.Text3;

            if (s.Available && s.On)
            {
                OriginalSnap o;
                s.ByTunex = App.Manager != null && App.Manager.State.Originals.TryGetValue(t.Id, out o) && !o.WasOn;
            }
            if (!s.Available) s.Status = s.Note;
            else if (s.On) s.Status = s.ByTunex ? "Применено через Tunex" : "Уже было сделано раньше";
            else if (s.AdminBlocked) { s.Status = "Не применено · нужны права администратора"; s.Color = Theme.Orange; }
            else if (t.Rec == Recommend.All) s.Status = "Не применено · рекомендуем";
            else if (t.Rec == Recommend.Some) s.Status = "Не применено · по ситуации";
            else s.Status = "Не применено · по желанию";
            return s;
        }
    }

    // ---------------------------------------------------------------- Сетка настроек (две колонки, клик открывает описание)
    class TweakCell : BufferedControl
    {
        public readonly Tweak Tweak;
        TweakState state;
        bool hover;

        readonly ToggleSwitch sw = new ToggleSwitch();
        bool busy;

        public TweakCell(Tweak t)
        {
            Tweak = t; Cursor = Cursors.Hand;
            Controls.Add(sw);
            // Переключатель сразу применяет настройку — тем же путём, что и окно подробностей (подтверждение, журнал, откат)
            sw.CheckedChanged += delegate
            {
                if (busy) return;
                busy = true; sw.Enabled = false;
                App.Form.ApplyTweak(Tweak, sw.Checked, delegate { busy = false; Sync(); });
            };
            Sync();
        }

        public void Sync()
        {
            state = TweakState.Of(Tweak);
            sw.Checked = state.Available && state.On;
            sw.Enabled = state.Available && !state.AdminBlocked && !busy;
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            sw.Location = new Point(Width - Pad - sw.Width, (Height - sw.Height) / 2);
        }

        static int Pad { get { return Theme.S(20); } }
        static int Box { get { return Theme.S(44); } }
        int TextW(int w) { return Math.Max(Theme.S(80), w - Pad * 3 - Box); }
        static int BadgeH { get { return Theme.Measure("Ag", Theme.FontSmallBold).Height + Theme.S(8); } }

        public int Measure(int w)
        {
            int h = Pad + Theme.Measure(Tweak.Title, Theme.FontCellTitle, TextW(w)).Height + Theme.S(8) + BadgeH + Pad;
            return Math.Max(Theme.S(76), h);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
        protected override void OnClick(EventArgs e) { base.OnClick(e); App.Form.ShowTweak(Tweak, this); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            if (hover)
                using (SolidBrush b = new SolidBrush(Theme.Alpha(Theme.CardInner, Theme.CardInner.A * 2 / 3))) g.FillRectangle(b, ClientRectangle);
            bool dim = !state.Available;
            int tw = TextW(Width);
            int th = Theme.Measure(Tweak.Title, Theme.FontCellTitle, tw).Height;
            int y = (Height - th - Theme.S(8) - BadgeH) / 2;
            Theme.DrawText(g, Tweak.Title, Theme.FontCellTitle, dim ? Theme.Text3 : Theme.Text, new Rectangle(Pad, y, tw, th), TextFormatFlags.WordBreak);

            // Статус — плашка своим цветом
            Color sc = dim ? Theme.Text3 : state.Color;
            string st = state.Status ?? "";
            int bw = Math.Min(tw, Theme.Measure(st, Theme.FontSmallBold).Width + Theme.S(18));
            Rectangle br = new Rectangle(Pad, y + th + Theme.S(8), bw, BadgeH);
            Theme.FillRound(g, Theme.Alpha(sc, dim ? 22 : 38), br, br.Height / 2f);
            Theme.DrawText(g, st, Theme.FontSmallBold, sc, br, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        }
    }

    class TweakGrid : BufferedControl
    {
        public string Title, Subtitle;
        readonly List<TweakCell> cells = new List<TweakCell>();
        readonly List<Rectangle> lines = new List<Rectangle>();
        int lastWidth = -1, headerH;

        Control accessory;
        public TweakGrid(string title, string subtitle) { Title = title; Subtitle = subtitle; }

        public Control Accessory
        {
            get { return accessory; }
            set { accessory = value; if (value != null) Controls.Add(value); Relayout(); }
        }

        int HeaderTextW { get { return Width - Theme.S(52) - (accessory != null ? accessory.Width + Theme.S(12) : 0); } }

        public void AddTweak(Tweak t) { if (t == null) return; TweakCell c = new TweakCell(t); cells.Add(c); Controls.Add(c); Relayout(); }

        public void SyncAll() { foreach (TweakCell c in cells) c.Sync(); Relayout(); }

        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); if (Width != lastWidth) { lastWidth = Width; Relayout(); } }

        public void Relayout()
        {
            if (Width <= 0) return;
            lines.Clear();
            int pad = Theme.S(20);
            headerH = 0;
            if (Title != null)
            {
                headerH = pad + Theme.Measure(Title, Theme.FontH2).Height;
                if (Subtitle != null) headerH += Theme.S(6) + Theme.Measure(Subtitle, Theme.FontSmall, HeaderTextW).Height;
                if (accessory != null) accessory.Location = new Point(Width - pad - accessory.Width + Theme.S(6), pad - Theme.S(6));
                headerH += pad;
                if (cells.Count > 0) lines.Add(new Rectangle(0, headerH, Width, 1));
                headerH += 1;
            }
            int cols = Width >= Theme.S(760) ? 2 : 1;
            int cw = Width / cols;
            int y = headerH;
            for (int i = 0; i < cells.Count; i += cols)
            {
                int rowH = 0;
                for (int c = 0; c < cols && i + c < cells.Count; c++) rowH = Math.Max(rowH, cells[i + c].Measure(c == cols - 1 ? Width - c * cw : cw));
                for (int c = 0; c < cols && i + c < cells.Count; c++)
                {
                    int x = c * cw, w = c == cols - 1 ? Width - x : cw;
                    cells[i + c].SetBounds(x, y, w, rowH);
                    if (c > 0) lines.Add(new Rectangle(x, y, 1, rowH));
                }
                y += rowH;
                if (i + cols < cells.Count) { lines.Add(new Rectangle(0, y, Width, 1)); y += 1; }
            }
            if (cells.Count == 0) y = headerH;
            if (Height != y) Height = y;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.PaintCard(g, r, Theme.Sf(14));
            int pad = Theme.S(20);
            if (Title != null)
            {
                int th = Theme.Measure(Title, Theme.FontH2).Height;
                Theme.DrawText(g, Title, Theme.FontH2, Theme.Text, new Rectangle(pad, pad, HeaderTextW, th), TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                if (Subtitle != null) Theme.DrawText(g, Subtitle, Theme.FontSmall, Theme.Text2, new Rectangle(pad, pad + th + Theme.S(6), HeaderTextW, Height), TextFormatFlags.WordBreak);
            }
            using (SolidBrush b = new SolidBrush(Theme.Border)) foreach (Rectangle l in lines) g.FillRectangle(b, l);
        }
    }

    // ---------------------------------------------------------------- Модальное окно поверх размытого интерфейса
    class ModalOverlay : BufferedControl
    {
        Bitmap backdrop; Control content;
        public event EventHandler Closed;

        public ModalOverlay() { Visible = false; BackColor = Theme.Bg; }

        public bool IsOpen { get { return Visible && content != null; } }

        public void Open(Form f, Control c)
        {
            if (content != null) CloseModal();
            Visible = false;
            try
            {
                using (Bitmap full = new Bitmap(Math.Max(1, f.ClientSize.Width), Math.Max(1, f.ClientSize.Height)))
                {
                    f.DrawToBitmap(full, new Rectangle(Point.Empty, f.ClientSize));
                    int sw = Math.Max(1, full.Width / 10), sh = Math.Max(1, full.Height / 10);
                    using (Bitmap small = new Bitmap(sw, sh))
                    {
                        using (Graphics g = Graphics.FromImage(small)) { g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.DrawImage(full, 0, 0, sw, sh); }
                        backdrop = new Bitmap(full.Width, full.Height);
                        using (Graphics g = Graphics.FromImage(backdrop))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.Half;
                            g.DrawImage(small, new Rectangle(0, 0, full.Width, full.Height));
                        }
                    }
                }
            }
            catch { backdrop = null; }
            Bounds = f.ClientRectangle;
            content = c;
            Controls.Add(c);
            Center();
            Visible = true;
            BringToFront();
            c.Focus();
        }

        public void CloseModal()
        {
            Visible = false;
            if (content != null) { Controls.Remove(content); content.Dispose(); content = null; }
            if (backdrop != null) { backdrop.Dispose(); backdrop = null; }
            if (Closed != null) Closed(this, EventArgs.Empty);
        }

        public void Center()
        {
            if (content == null) return;
            content.Location = new Point(Math.Max(0, (Width - content.Width) / 2), Math.Max(Theme.S(20), (Height - content.Height) / 2));
        }

        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Center(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            DialogCard d = content as DialogCard;
            if (content != null && !content.Bounds.Contains(e.Location) && (d == null || d.CloseOnOutsideClick)) CloseModal();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (backdrop != null) g.DrawImageUnscaled(backdrop, 0, 0);
            using (SolidBrush b = new SolidBrush(backdrop != null ? Theme.Scrim : Theme.Alpha(Theme.Scrim, 220))) g.FillRectangle(b, ClientRectangle);
        }
    }

    class DialogCard : BufferedControl
    {
        public bool CloseOnOutsideClick = true;
        protected readonly List<int> Separators = new List<int>();
        protected static int Pad { get { return Theme.S(24); } }

        public DialogCard() { Width = Theme.S(580); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            // Окно поверх затемнения — непрозрачное, скругление 18 px (gw-sheet)
            Theme.FillRound(g, Theme.Pop, r, Theme.Sf(18));
            Theme.DrawRound(g, Theme.Frame, r, Theme.Sf(18), 1f);
            using (Pen p = new Pen(Theme.Border)) foreach (int y in Separators) g.DrawLine(p, 1, y, Width - 2, y);
            PaintContent(g);
        }

        protected virtual void PaintContent(Graphics g) { }

        protected static void Chip(Graphics g, string glyph, string text, Color color, ref int x, int y)
        {
            Size ts = Theme.Measure(text, Theme.FontSmall);
            int h = Theme.S(36), w = ts.Width + Theme.S(glyph != null ? 52 : 28);
            Rectangle r = new Rectangle(x, y, w, h);
            Theme.FillRound(g, Theme.CardInner, r, h / 2f);
            int tx = x + Theme.S(14);
            if (glyph != null) { Theme.Glyph(g, glyph, Theme.FontIconSmall, color, new Rectangle(tx, y, Theme.S(16), h)); tx += Theme.S(24); }
            Theme.DrawText(g, text, Theme.FontSmall, color == Theme.Text2 ? Theme.Text : color, new Rectangle(tx, y, ts.Width + 4, h), TextFormatFlags.VerticalCenter);
            x += w + Theme.S(8);
        }

        protected static int ChipsHeight(List<string> texts, int width)
        {
            int x = 0, rows = texts.Count > 0 ? 1 : 0;
            foreach (string t in texts)
            {
                int w = Theme.Measure(t, Theme.FontSmall).Width + Theme.S(60);
                if (x > 0 && x + w > width) { rows++; x = 0; }
                x += w;
            }
            return rows == 0 ? 0 : rows * Theme.S(36) + (rows - 1) * Theme.S(8);
        }
    }

    // ---------------------------------------------------------------- Окно настройки: переключатель + «Подробнее»
    class TweakDialog : DialogCard
    {
        readonly Tweak tweak;
        readonly ToggleSwitch toggle;
        readonly IconButton close;
        readonly FlatButton primary, secondary;
        readonly bool wizard;
        readonly string stepText;
        TweakState state;
        bool busy;
        int headerBottom, detailsTop;
        readonly List<KeyValuePair<string, Color>> chips = new List<KeyValuePair<string, Color>>();
        public Action<bool> WizardAnswer; // true — применить, false — пропустить

        public TweakDialog(Tweak t, int step, int total)
        {
            tweak = t; wizard = total > 0;
            stepText = wizard ? "Шаг " + step + " из " + total : null;
            close = new IconButton(Icons.Close, Theme.S(34));
            close.Click += delegate { App.Form.CloseModal(); };
            Controls.Add(close);
            if (!wizard)
            {
                toggle = new ToggleSwitch();
                toggle.CheckedChanged += delegate { OnToggle(); };
                Controls.Add(toggle);
            }
            else
            {
                primary = new FlatButton("Применить", true, Icons.Check);
                secondary = new FlatButton("Пропустить", false);
                primary.Click += delegate { if (WizardAnswer != null) WizardAnswer(true); };
                secondary.Click += delegate { if (WizardAnswer != null) WizardAnswer(false); };
                Controls.Add(primary); Controls.Add(secondary);
            }
            Refresh_();
        }

        void Refresh_()
        {
            state = TweakState.Of(tweak);
            if (toggle != null)
            {
                toggle.Checked = state.On;
                toggle.Enabled = state.Available && !state.AdminBlocked && !busy;
            }
            if (primary != null) primary.Enabled = state.Available && !state.AdminBlocked && !busy;
            chips.Clear();
            chips.Add(new KeyValuePair<string, Color>(state.Status, state.Available ? state.Color : Theme.Orange));
            if (tweak.RecText != null) chips.Add(new KeyValuePair<string, Color>(tweak.RecText, Theme.Text2));
            if (tweak.NeedsReboot) chips.Add(new KeyValuePair<string, Color>("Заработает после перезагрузки", Theme.Purple));
            if (state.Note != null && state.Available) chips.Add(new KeyValuePair<string, Color>(state.Note, Theme.Orange));
            if (busy) chips.Add(new KeyValuePair<string, Color>("Применяю…", Theme.Accent));
            Relayout();
        }

        void Relayout()
        {
            Separators.Clear();
            int w = Width - Pad * 2;
            int top = Pad;
            if (stepText != null) top += Theme.Measure(stepText, Theme.FontSmallBold).Height + Theme.S(10);
            int textW = w - Theme.S(120);
            int th = Theme.Measure(tweak.Title, Theme.FontH2, textW).Height;
            int dh = Theme.Measure(tweak.Description, Theme.FontBody, textW).Height;
            headerBottom = top + th + Theme.S(6) + dh + Pad;
            close.Location = new Point(Width - Pad - close.Width + Theme.S(6), top - Theme.S(4));
            if (toggle != null) toggle.Location = new Point(close.Left - Theme.S(12) - toggle.Width, top);
            Separators.Add(headerBottom);
            detailsTop = headerBottom + Pad;
            int y = detailsTop + Theme.Measure("Подробнее", Theme.FontBodyBold).Height + Theme.S(8);
            y += Theme.Measure(tweak.Details ?? "", Theme.FontBody, w).Height + Theme.S(18);
            List<string> texts = new List<string>();
            foreach (KeyValuePair<string, Color> c in chips) texts.Add(c.Key);
            y += ChipsHeight(texts, w);
            y += Pad;
            if (wizard)
            {
                Separators.Add(y);
                y += Theme.S(18);
                primary.Location = new Point(Width - Pad - primary.Width, y);
                secondary.Location = new Point(primary.Left - Theme.S(10) - secondary.Width, y);
                y += primary.Height + Theme.S(18);
            }
            Height = y;
            if (Parent != null) ((ModalOverlay)Parent).Center();
            Invalidate();
        }

        protected override void PaintContent(Graphics g)
        {
            int w = Width - Pad * 2, top = Pad;
            if (stepText != null)
            {
                Theme.DrawText(g, stepText, Theme.FontSmallBold, Theme.Accent, new Rectangle(Pad, top, w, Theme.S(20)), TextFormatFlags.Left);
                top += Theme.Measure(stepText, Theme.FontSmallBold).Height + Theme.S(10);
            }
            int textW = w - Theme.S(120);
            int th = Theme.Measure(tweak.Title, Theme.FontH2, textW).Height;
            Theme.DrawText(g, tweak.Title, Theme.FontH2, Theme.Text, new Rectangle(Pad, top, textW, th), TextFormatFlags.WordBreak);
            Theme.DrawText(g, tweak.Description, Theme.FontBody, Theme.Text2, new Rectangle(Pad, top + th + Theme.S(6), textW, Height), TextFormatFlags.WordBreak);

            int y = detailsTop;
            int lh = Theme.Measure("Подробнее", Theme.FontBodyBold).Height;
            Theme.DrawText(g, "Подробнее", Theme.FontBodyBold, Theme.Text, new Rectangle(Pad, y, w, lh), TextFormatFlags.Left);
            y += lh + Theme.S(8);
            int dh = Theme.Measure(tweak.Details ?? "", Theme.FontBody, w).Height;
            Theme.DrawText(g, tweak.Details ?? "", Theme.FontBody, Theme.Text, new Rectangle(Pad, y, w, dh), TextFormatFlags.WordBreak);
            y += dh + Theme.S(18);
            int x = Pad;
            foreach (KeyValuePair<string, Color> c in chips)
            {
                int cw = Theme.Measure(c.Key, Theme.FontSmall).Width + Theme.S(52);
                if (x > Pad && x + cw > Pad + w) { x = Pad; y += Theme.S(44); }
                Chip(g, c.Value == Theme.Text2 ? Icons.Info : c.Value == Theme.Purple ? Icons.Refresh : c.Value == Theme.Green ? Icons.Check : Icons.Warning, c.Key, c.Value, ref x, y);
            }
        }

        void OnToggle()
        {
            bool want = toggle.Checked;
            busy = true; Refresh_();
            toggle.Checked = want;
            App.Form.ApplyTweak(tweak, want, delegate { busy = false; if (!IsDisposed) Refresh_(); });
        }
    }

    // ---------------------------------------------------------------- Окно проверки производительности
    class BenchDialog : DialogCard
    {
        string stage = "Подготовка…"; int percent; volatile bool cancel;
        BenchResult result, previous;
        readonly FlatButton button;

        // Плавный ползунок: этап сообщает только начало, а между этапами полоса едет сама
        // с ожидаемой длительностью этапа и не обгоняет следующий этап
        readonly System.Windows.Forms.Timer smooth = new System.Windows.Forms.Timer();
        float shown;                    // что нарисовано, 0..100
        int stageFrom, stageTo = 5, stageMs = 400;
        DateTime stageStart = DateTime.Now;

        static void NextStage(int p, out int to, out int ms)
        {
            if (p < 30) { to = 30; ms = 3600; }        // одно ядро
            else if (p < 60) { to = 60; ms = 4200; }   // все ядра
            else if (p < 75) { to = 75; ms = 3200; }   // память
            else if (p < 100) { to = 99; ms = 5000; }  // диск
            else { to = 100; ms = 1; }
        }

        void OnSmooth()
        {
            double t = (DateTime.Now - stageStart).TotalMilliseconds / stageMs;
            // быстро в начале этапа, медленнее к концу; если этап затянулся — почти останавливается перед следующим
            double k = 1 - Math.Exp(-2.2 * t);
            float target = (float)(stageFrom + (stageTo - stageFrom) * k);
            if (percent >= 100) target = 100;
            float next = shown + (target - shown) * 0.25f;
            if (Math.Abs(next - shown) > 0.01f) { shown = Math.Max(shown, next); Invalidate(); }
        }

        public BenchDialog()
        {
            CloseOnOutsideClick = false;
            List<BenchResult> list = App.Manager.State.Benchmarks;
            previous = list.Count > 0 ? list[list.Count - 1] : null;
            button = new FlatButton("Отменить", false);
            button.Click += delegate
            {
                if (result == null) cancel = true;
                App.Form.CloseModal();
            };
            Controls.Add(button);
            Height = Theme.S(250);
            Layout_();
            Thread th = new Thread(delegate()
            {
                BenchResult r = null;
                try
                {
                    r = Bench.Run(delegate(string s, int p)
                    {
                        if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(delegate
                        {
                            stage = s; percent = p;
                            stageFrom = Math.Max(p, (int)shown); stageStart = DateTime.Now;
                            NextStage(p, out stageTo, out stageMs);
                            Invalidate();
                        }));
                    }, delegate { return cancel; });
                }
                catch { }
                if (cancel || IsDisposed) return;
                try { BeginInvoke(new Action(delegate { Done(r); })); } catch { }
            });
            th.IsBackground = true;
            HandleCreated += delegate { th.Start(); smooth.Start(); };
            smooth.Interval = 30;
            smooth.Tick += delegate { OnSmooth(); };
        }

        void Done(BenchResult r)
        {
            smooth.Stop();
            result = r;
            if (r != null)
            {
                List<BenchResult> list = App.Manager.State.Benchmarks;
                list.Add(r);
                while (list.Count > 30) list.RemoveAt(0);
                App.Manager.State.Save();
            }
            button.Text = "Готово"; button.Primary = true;
            Height = Theme.S(r != null ? 560 : 250);
            Layout_();
            if (Parent != null) ((ModalOverlay)Parent).Center();
            Invalidate();
            App.Form.RefreshCurrent();
        }

        protected override void Dispose(bool disposing) { cancel = true; smooth.Stop(); smooth.Dispose(); base.Dispose(disposing); }

        void Layout_() { button.Location = new Point(Width - Pad - button.Width, Height - Pad - button.Height); }

        static string Diff(int now, int before)
        {
            if (before <= 0) return "";
            double d = (now - before) * 100.0 / before;
            if (Math.Abs(d) < 1.5) return "  ≈ как в прошлый раз";
            return "  " + (d > 0 ? "+" : "") + d.ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        protected override void PaintContent(Graphics g)
        {
            int w = Width - Pad * 2, y = Pad;
            Theme.DrawText(g, "Проверка производительности", Theme.FontH2, Theme.Text, new Rectangle(Pad, y, w, Theme.S(30)), TextFormatFlags.Left);
            y += Theme.S(36);
            if (result == null)
            {
                Theme.DrawText(g, "Не пользуйтесь компьютером около 20 секунд — так результат будет точнее.", Theme.FontBody, Theme.Text2, new Rectangle(Pad, y, w, Theme.S(44)), TextFormatFlags.WordBreak);
                y += Theme.S(52);
                Theme.DrawText(g, stage, Theme.FontBodyBold, Theme.Text, new Rectangle(Pad, y, w, Theme.S(24)), TextFormatFlags.Left);
                Theme.DrawText(g, (int)shown + "%", Theme.FontBodyBold, Theme.Accent, new Rectangle(Pad, y, w, Theme.S(24)), TextFormatFlags.Right);
                y += Theme.S(32);
                RectangleF track = new RectangleF(Pad, y, w, Theme.Sf(8));
                Theme.FillRound(g, Theme.CardInner, track, Theme.Sf(4));
                if (shown > 0.5f) Theme.FillRound(g, Theme.Accent, new RectangleF(Pad, y, Math.Max(Theme.Sf(8), w * shown / 100f), Theme.Sf(8)), Theme.Sf(4));
                return;
            }
            // Итог
            Theme.DrawText(g, result.Total.ToString(), Theme.FontTitle, Theme.Text, new Rectangle(Pad, y, w, Theme.S(48)), TextFormatFlags.Left);
            int tw = Theme.Measure(result.Total.ToString(), Theme.FontTitle).Width;
            Theme.DrawText(g, "очков · " + Bench.Tier(result.Total) + " для игр" + (previous != null ? Diff(result.Total, previous.Total) : ""), Theme.FontBody, Theme.Text2,
                new Rectangle(Pad + tw + Theme.S(10), y + Theme.S(8), w - tw, Theme.S(26)), TextFormatFlags.Left);
            y += Theme.S(40);
            string verdict = Bench.Verdict(result);
            int vh = Theme.Measure(verdict, Theme.FontSmall, w).Height;
            Theme.DrawText(g, verdict, Theme.FontSmall, Theme.Text2, new Rectangle(Pad, y, w, vh), TextFormatFlags.WordBreak);
            y += vh + Theme.S(14);
            string[][] rows =
            {
                new string[] { "Процессор, одно ядро", result.CpuSingle + Diff(result.CpuSingle, previous != null ? previous.CpuSingle : 0), "Важно для большинства игр" },
                new string[] { "Процессор, все ядра", result.CpuMulti + Diff(result.CpuMulti, previous != null ? previous.CpuMulti : 0), result.Threads + " потоков" },
                new string[] { "Оперативная память", result.RamScore + Diff(result.RamScore, previous != null ? previous.RamScore : 0), result.RamGBps.ToString("0.0", CultureInfo.InvariantCulture) + " ГБ/с" },
                new string[] { "Диск", result.DiskScore > 0 ? result.DiskScore + Diff(result.DiskScore, previous != null ? previous.DiskScore : 0) : "—",
                    result.DiskScore > 0 ? "чтение " + result.DiskReadMBps.ToString("0") + " МБ/с, запись " + result.DiskWriteMBps.ToString("0") + " МБ/с" : "не удалось проверить" },
                new string[] { "Видеокарта", result.GpuScore.ToString(), (result.GpuIntegrated ? "встроенная, " : "") + (result.GpuKnown ? "оценка по модели" : "модель неизвестна, оценка по видеопамяти") + " · самое важное для игр" }
            };
            using (Pen p = new Pen(Theme.Border))
                foreach (string[] r in rows)
                {
                    g.DrawLine(p, Pad, y, Width - Pad, y);
                    y += Theme.S(12);
                    Theme.DrawText(g, r[0], Theme.FontBodyBold, Theme.Text, new Rectangle(Pad, y, w / 2, Theme.S(22)), TextFormatFlags.Left);
                    Theme.DrawText(g, r[2], Theme.FontSmall, Theme.Text3, new Rectangle(Pad, y + Theme.S(22), w / 2 + Theme.S(60), Theme.S(20)), TextFormatFlags.Left);
                    Theme.DrawText(g, r[1], Theme.FontBodyBold, Theme.Accent, new Rectangle(Pad + w / 2, y + Theme.S(8), w / 2, Theme.S(24)), TextFormatFlags.Right);
                    y += Theme.S(52);
                }
            Theme.DrawText(g, "1000 очков — сильный игровой ПК (уровень RTX 4070). Видеокарта весит больше всего.", Theme.FontSmall, Theme.Text3,
                new Rectangle(Pad, y + Theme.S(6), w - button.Width - Theme.S(20), Theme.S(40)), TextFormatFlags.WordBreak);
        }
    }

    // ---------------------------------------------------------------- Верхний блок дашборда
    class PerfBlock : BufferedControl
    {
        BenchResult bench;
        int optPercent;
        List<Color> segments = new List<Color>();
        public readonly FlatButton StartButton;
        Rectangle left, right;
        bool stacked;

        public PerfBlock()
        {
            StartButton = new FlatButton("Начать", true);
            Controls.Add(StartButton);
            Height = Theme.S(184);
        }

        public void SetData(BenchResult b, int pct, List<Color> segs) { bench = b; optPercent = pct; segments = segs; Invalidate(); }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            int gap = Theme.S(12), h = Theme.S(184);
            stacked = Width < Theme.S(780);
            if (stacked)
            {
                left = new Rectangle(0, 0, Width, h);
                right = new Rectangle(0, h + gap, Width, Theme.S(164));
                if (Height != right.Bottom + 1) Height = right.Bottom + 1;
            }
            else
            {
                int lw = (int)((Width - gap) * 0.62);
                left = new Rectangle(0, 0, lw, h);
                right = new Rectangle(lw + gap, 0, Width - lw - gap, h);
                if (Height != h + 1) Height = h + 1;
            }
            StartButton.Location = new Point(right.Right - Theme.S(24) - StartButton.Width, right.Y + right.Height * 3 / 4 - StartButton.Height / 2);
        }

        static void Box(Graphics g, Rectangle r)
        {
            RectangleF rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1.5f, r.Height - 1.5f);
            Theme.PaintCard(g, rf, Theme.Sf(14));
        }

        static void Cell(Graphics g, Rectangle r, string value, string label, Color valueColor)
        {
            int p = Theme.S(22);
            int vh = Theme.Measure("0", Theme.FontH2).Height;
            string l = Theme.Track(label);
            int lh = Theme.Measure(l, Theme.FontLabel).Height;
            int y = r.Y + (r.Height - vh - lh - Theme.S(6)) / 2;
            Theme.DrawText(g, l, Theme.FontLabel, Theme.Text2, new Rectangle(r.X + p, y, r.Width - p * 2, lh + 2), TextFormatFlags.EndEllipsis);
            Theme.DrawText(g, value, Theme.FontH2, valueColor, new Rectangle(r.X + p, y + lh + Theme.S(6), r.Width - p * 2, vh), TextFormatFlags.EndEllipsis);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            Box(g, left); Box(g, right);
            int c0 = (int)(left.Width * 0.36), cw = (left.Width - c0) / 2, rh = left.Height / 2;
            using (Pen p = new Pen(Theme.Border))
            {
                g.DrawLine(p, left.X + c0, left.Y + 1, left.X + c0, left.Bottom - 2);
                g.DrawLine(p, left.X + c0 + cw, left.Y + 1, left.X + c0 + cw, left.Bottom - 2);
                g.DrawLine(p, left.X + c0, left.Y + rh, left.Right - 2, left.Y + rh);
                g.DrawLine(p, right.X + 1, right.Y + right.Height / 2, right.Right - 2, right.Y + right.Height / 2);
            }

            // Рейтинг с кривой распределения
            Rectangle rate = new Rectangle(left.X, left.Y, c0, left.Height);
            int pad = Theme.S(22);
            Theme.DrawText(g, "Игровой рейтинг", Theme.FontLabel, Theme.Text2, new Rectangle(rate.X + pad, rate.Y + pad, rate.Width - pad, Theme.S(16)), TextFormatFlags.Left);
            string big = bench != null ? bench.Total.ToString() : "—";
            Theme.DrawText(g, big, Theme.FontBig, Theme.Text, new Rectangle(rate.X + pad - Theme.S(1), rate.Y + pad + Theme.S(16), rate.Width - pad, Theme.S(36)), TextFormatFlags.Left);
            if (bench != null)
            {
                int bw = Theme.Measure(big, Theme.FontBig).Width;
                Color tc = TierColor(bench.Total);
                Theme.DrawText(g, Bench.Tier(bench.Total).ToLowerInvariant(), Theme.FontSmallBold, tc,
                    new Rectangle(rate.X + pad + bw + Theme.S(8), rate.Y + pad + Theme.S(16), rate.Width, Theme.S(32)), TextFormatFlags.Bottom);
            }
            DrawCurve(g, new Rectangle(rate.X + 1, rate.Y + rate.Height / 2, rate.Width - 2, rate.Height / 2 - 1));

            string cpu = bench != null ? bench.Cpu.ToString() : "—";
            string ram = bench != null ? bench.RamScore.ToString() : "—";
            string disk = bench != null && bench.DiskScore > 0 ? bench.DiskScore.ToString() : "—";
            string last = bench != null ? FormatDate(bench.When) : "Не было";
            Cell(g, new Rectangle(left.X + c0, left.Y, cw, rh), cpu, "Процессор", Theme.Text);
            Cell(g, new Rectangle(left.X + c0 + cw, left.Y, left.Width - c0 - cw, rh), bench != null ? bench.GpuScore + "  ·  " + Bench.GpuTier().ToLowerInvariant() : SysInfo.HardwareLoaded ? Bench.GpuTier() : "…", "Видеокарта", Theme.Text);
            Cell(g, new Rectangle(left.X + c0, left.Y + rh, cw, left.Height - rh), ram + "  /  " + disk, "Память  /  Диск", Theme.Text);
            Cell(g, new Rectangle(left.X + c0 + cw, left.Y + rh, left.Width - c0 - cw, left.Height - rh), last, "Проверка", Theme.Text);

            // Оптимизация
            Rectangle top = new Rectangle(right.X, right.Y, right.Width, right.Height / 2);
            Rectangle bottom = new Rectangle(right.X, right.Y + right.Height / 2, right.Width, right.Height - right.Height / 2);
            int barsW = Math.Min(Theme.S(200), right.Width / 2 - pad);
            Cell(g, new Rectangle(top.X, top.Y, top.Width - barsW - pad, top.Height), optPercent + "%", "Оптимизация", optPercent >= 80 ? Theme.Green : optPercent >= 40 ? Theme.Orange : Theme.Red);
            if (segments.Count > 0)
            {
                float sw = Theme.Sf(6), gap = Theme.Sf(4);
                float need = segments.Count * sw + (segments.Count - 1) * gap;
                if (need > barsW) { sw = (barsW - (segments.Count - 1) * gap) / segments.Count; need = barsW; }
                float x = top.Right - pad - need, bh = Theme.Sf(40), by = top.Y + (top.Height - bh) / 2;
                foreach (Color c in segments) { Theme.FillRound(g, c, new RectangleF(x, by, Math.Max(2, sw), bh), Math.Min(sw / 2, Theme.Sf(3))); x += sw + gap; }
            }
            int lw = StartButton.Left - bottom.X - pad * 2;
            int y = bottom.Y + (bottom.Height - Theme.S(46)) / 2;
            Theme.DrawText(g, "Пошаговая настройка", Theme.FontBodyBold, Theme.Text, new Rectangle(bottom.X + pad, y, lw, Theme.S(24)), TextFormatFlags.EndEllipsis);
            Theme.DrawText(g, "Рекомендации по одной", Theme.FontSmall, Theme.Text2, new Rectangle(bottom.X + pad, y + Theme.S(24), lw, Theme.S(22)), TextFormatFlags.EndEllipsis);
        }

        static Color TierColor(int score)
        {
            if (score < 450) return Theme.Red;
            if (score < 700) return Theme.Orange;
            if (score < 1000) return Theme.Green;
            return Theme.Accent;
        }

        // Шкала прибора 0…1500 с цветными зонами и стрелкой-меткой
        void DrawCurve(Graphics g, Rectangle r)
        {
            int p = Theme.S(22);
            float x0 = r.X + p, x1 = r.Right - p, w = x1 - x0;
            float baseY = r.Bottom - Theme.S(30);
            const float Max = 1500f;
            int[] zones = { 0, 450, 700, 1000, 1500 };
            Color[] zc = { Theme.Red, Theme.Orange, Theme.Green, Theme.Accent };
            for (int i = 0; i < 4; i++)
            {
                float a = x0 + w * zones[i] / Max, b = x0 + w * zones[i + 1] / Max;
                using (SolidBrush zb = new SolidBrush(Theme.Alpha(zc[i], bench != null ? 170 : 70))) g.FillRectangle(zb, a, baseY, b - a - (i < 3 ? Theme.Sf(2) : 0), Theme.Sf(3));
            }
            using (Pen tick = new Pen(Theme.Border))
            using (Pen major = new Pen(Theme.Text3))
                for (int v = 0; v <= 1500; v += 100)
                {
                    float x = x0 + w * v / Max;
                    bool big = v % 500 == 0;
                    g.DrawLine(big ? major : tick, x, baseY - Theme.S(big ? 9 : 5), x, baseY - Theme.S(2));
                    if (big)
                    {
                        string s = v.ToString();
                        int sw = Theme.Measure(s, Theme.FontLabel).Width;
                        int lx = v == 0 ? (int)x : v == 1500 ? (int)x - sw : (int)x - sw / 2;
                        Theme.DrawText(g, s, Theme.FontLabel, Theme.Text3, new Rectangle(lx, (int)baseY + Theme.S(8), sw + 4, Theme.S(14)), TextFormatFlags.Left);
                    }
                }
            if (bench != null)
            {
                float mx = x0 + w * Math.Min(bench.Total, Max) / Max;
                using (Pen pen = new Pen(Theme.Text, Theme.Sf(2))) g.DrawLine(pen, mx, baseY - Theme.S(18), mx, baseY + Theme.S(3));
                PointF[] tri = { new PointF(mx - Theme.Sf(5), baseY - Theme.S(24)), new PointF(mx + Theme.Sf(5), baseY - Theme.S(24)), new PointF(mx, baseY - Theme.S(17)) };
                using (SolidBrush tb = new SolidBrush(Theme.Text)) g.FillPolygon(tb, tri);
            }
        }

        static string FormatDate(DateTime d)
        {
            if (d.Date == DateTime.Today) return "Сегодня, " + d.ToString("HH:mm");
            if (d.Date == DateTime.Today.AddDays(-1)) return "Вчера, " + d.ToString("HH:mm");
            return d.ToString("d MMM", new CultureInfo("ru-RU"));
        }
    }

    // ---------------------------------------------------------------- Поле ввода в тёмной рамке
    class DarkTextBox : BufferedControl
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        public readonly TextBox Box = new TextBox();
        public event Action Submit;
        readonly string placeholder;

        public DarkTextBox(int width, string hint)
        {
            placeholder = hint;
            Size = new Size(width, Theme.S(34));
            Cursor = Cursors.IBeam;
            Box.BorderStyle = BorderStyle.None;
            Box.BackColor = Theme.Field; Box.ForeColor = Theme.Text; Box.Font = Theme.FontBody;
            Controls.Add(Box);
            Box.KeyDown += delegate(object o, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                if (Submit != null) Submit();
            };
            Box.GotFocus += delegate { Invalidate(); };
            Box.LostFocus += delegate { Invalidate(); };
            Box.HandleCreated += delegate { if (placeholder != null) SendMessage(Box.Handle, 0x1501, (IntPtr)1, placeholder); };
        }

        public override string Text { get { return Box.Text; } set { Box.Text = value; } }

        protected override void OnClick(EventArgs e) { base.OnClick(e); Box.Focus(); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            Box.SetBounds(Theme.S(10), (Height - Box.PreferredHeight) / 2 + 1, Width - Theme.S(20), Box.PreferredHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            // gw-field: светлое поле с тонкой рамкой, в фокусе — рамка цветом акцента и мягкое кольцо
            if (Box.Focused) Theme.DrawRound(g, Theme.Alpha(Theme.Accent, 90), new RectangleF(r.X - 0.5f, r.Y - 0.5f, r.Width + 1, r.Height + 1), Theme.Sf(11), Theme.Sf(2));
            Theme.FillRound(g, Theme.Field, r, Theme.Sf(10));
            Theme.DrawRound(g, Box.Focused ? Theme.Accent : Theme.FieldEdge, r, Theme.Sf(10), 1f);
        }
    }

    // ---------------------------------------------------------------- Таблица: заголовок, строки, подсветка, щелчок по строке
    class TableColumn
    {
        public string Title; public float Weight; public bool Right;
        public TableColumn(string title, float weight, bool right) { Title = title; Weight = weight; Right = right; }
    }

    class TableCell
    {
        public string Text, Sub; public Color Color = Theme.Text; public bool Badge, Bold;
        public TableCell(string text) { Text = text; }
        public static TableCell Of(string text, Color c) { TableCell x = new TableCell(text); x.Color = c; return x; }
        public static TableCell Pill(string text, Color c) { TableCell x = Of(text, c); x.Badge = true; return x; }
    }

    class TableView : BufferedControl
    {
        public readonly List<TableColumn> Columns = new List<TableColumn>();
        public readonly List<TableCell[]> Rows = new List<TableCell[]>();
        public int Highlight = -1;
        public bool Clickable;
        public string Footer;
        public event Action<int> RowClick;
        int hover = -1;

        // Группы: строка-заголовок сворачивает строки под ней. kind: -1 — обычная строка вне групп, -2 — заголовок, иначе номер строки-заголовка
        readonly List<int> kind = new List<int>();
        readonly HashSet<string> open = new HashSet<string>();   // раскрытые группы — по названию, чтобы пережить перестройку таблицы
        int currentGroup = -1;

        public void ClearRows() { Rows.Clear(); kind.Clear(); currentGroup = -1; Highlight = -1; hover = -1; }

        public void AddGroup(TableCell[] header)
        {
            Rows.Add(header); kind.Add(-2); currentGroup = Rows.Count - 1;
        }

        public int AddRow(TableCell[] cells, bool inGroup)
        {
            Rows.Add(cells); kind.Add(inGroup ? currentGroup : -1);
            return Rows.Count - 1;
        }

        int KindOf(int i) { return i < kind.Count ? kind[i] : -1; }
        bool IsGroup(int i) { return KindOf(i) == -2; }
        string GroupKey(int i) { return Rows[i].Length > 0 && Rows[i][0] != null ? Rows[i][0].Text : ""; }
        public bool IsOpen(int headerRow) { return open.Contains(GroupKey(headerRow)); }
        public void SetOpen(string title, bool on) { if (on) open.Add(title); else open.Remove(title); }

        List<int> VisibleRows()
        {
            List<int> v = new List<int>();
            for (int i = 0; i < Rows.Count; i++)
            {
                int k = KindOf(i);
                if (k >= 0 && !IsOpen(k)) continue;
                v.Add(i);
            }
            return v;
        }

        int HeadH { get { return Theme.S(38); } }
        int RowH
        {
            get
            {
                foreach (TableCell[] r in Rows) foreach (TableCell c in r) if (c != null && c.Sub != null) return Theme.S(56);
                return Theme.S(44);
            }
        }
        int Pad { get { return Theme.S(18); } }
        int FooterH { get { return string.IsNullOrEmpty(Footer) ? 0 : Theme.Measure(Footer, Theme.FontSmall, Math.Max(Theme.S(100), Width - Pad * 2)).Height + Theme.S(26); } }
        int Wanted { get { return HeadH + VisibleRows().Count * RowH + FooterH + 1; } }

        public void Done()
        {
            int h = Wanted;
            if (Height != h) Height = h; else Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); int h = Wanted; if (Height != h) Height = h; Invalidate(); }

        int[] ColumnX()
        {
            float total = 0; foreach (TableColumn c in Columns) total += c.Weight;
            int avail = Width - Pad * 2 - Theme.S(28);
            int[] x = new int[Columns.Count + 1];
            x[0] = Pad;
            for (int i = 0; i < Columns.Count; i++) x[i + 1] = x[i] + (int)(avail * Columns[i].Weight / total);
            return x;
        }

        int At(Point p)
        {
            if (p.Y < HeadH) return -1;
            List<int> v = VisibleRows();
            int slot = (p.Y - HeadH) / RowH;
            return slot >= 0 && slot < v.Count ? v[slot] : -1;
        }

        bool Active(int i) { return i >= 0 && (IsGroup(i) || Clickable); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = At(e.Location);
            if (!Active(h)) h = -1;
            if (h != hover) { hover = h; Invalidate(); }
            Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hover != -1) { hover = -1; Invalidate(); } }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int i = At(e.Location);
            if (i < 0) return;
            if (IsGroup(i)) { SetOpen(GroupKey(i), !IsOpen(i)); hover = -1; Done(); return; }
            if (Clickable && RowClick != null) RowClick(i);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            RectangleF card = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRound(g, Theme.Card, card, Theme.Sf(14));
            GraphicsPath clip = Theme.RoundPath(card, Theme.Sf(14));
            g.SetClip(clip);
            int[] x = ColumnX();
            int rh = RowH;
            List<int> vis = VisibleRows();

            // Заголовок: обычными буквами, серым
            for (int c = 0; c < Columns.Count; c++)
            {
                string t = Columns[c].Title;
                Rectangle r = new Rectangle(x[c], 0, x[c + 1] - x[c] - Theme.S(10), HeadH);
                Theme.DrawText(g, t, Theme.FontLabel, Theme.Text2, r, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | (Columns[c].Right ? TextFormatFlags.Right : TextFormatFlags.Left));
            }

            using (Pen sep = new Pen(Theme.Border))
            {
                g.DrawLine(sep, 1, HeadH, Width - 2, HeadH);
                for (int s = 0; s < vis.Count; s++)
                {
                    int i = vis[s];
                    int y = HeadH + s * rh;
                    bool grp = IsGroup(i);
                    if (grp)
                        using (SolidBrush b = new SolidBrush(i == hover ? Theme.CardHover : Theme.CardInner)) g.FillRectangle(b, 1, y + 1, Width - 2, rh - 1);
                    else if (i == Highlight)
                        using (SolidBrush b = new SolidBrush(Theme.AccentSoft)) g.FillRectangle(b, 1, y + 1, Width - 2, rh - 1);
                    else if (i == hover)
                        using (SolidBrush b = new SolidBrush(Theme.CardInner)) g.FillRectangle(b, 1, y + 1, Width - 2, rh - 1);
                    if (s > 0) g.DrawLine(sep, grp ? 1 : Pad, y, grp ? Width - 2 : Width - Pad, y);

                    TableCell[] row = Rows[i];
                    int indent = KindOf(i) >= 0 ? Theme.S(16) : 0;
                    for (int c = 0; c < Columns.Count && c < row.Length; c++)
                    {
                        TableCell cell = row[c];
                        if (cell == null || string.IsNullOrEmpty(cell.Text)) continue;
                        Rectangle r = new Rectangle(x[c] + (c == 0 ? indent : 0), y, x[c + 1] - x[c] - Theme.S(10) - (c == 0 ? indent : 0), rh);
                        TextFormatFlags al = Columns[c].Right ? TextFormatFlags.Right : TextFormatFlags.Left;
                        if (cell.Badge)
                        {
                            Size bs = Theme.Measure(cell.Text, Theme.FontSmallBold);
                            int bw = Math.Min(bs.Width + Theme.S(16), r.Width), bh = bs.Height + Theme.S(6);
                            Rectangle br = new Rectangle(Columns[c].Right ? r.Right - bw : r.X, y + (rh - bh) / 2, bw, bh);
                            Theme.FillRound(g, Theme.Alpha(cell.Color, 40), br, bh / 2f);
                            Theme.DrawText(g, cell.Text, Theme.FontSmallBold, cell.Color, br, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                            continue;
                        }
                        Font f = cell.Bold ? Theme.FontBodyBold : Theme.FontBody;
                        if (cell.Sub == null)
                            Theme.DrawText(g, cell.Text, f, cell.Color, r, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | al);
                        else
                        {
                            int th = Theme.Measure("Ag", f).Height, sh = Theme.Measure("Ag", Theme.FontSmall).Height;
                            int ty = y + (rh - th - sh - Theme.S(2)) / 2;
                            Theme.DrawText(g, cell.Text, f, cell.Color, new Rectangle(r.X, ty, r.Width, th), TextFormatFlags.EndEllipsis | al);
                            Theme.DrawText(g, cell.Sub, Theme.FontSmall, Theme.Text3, new Rectangle(r.X, ty + th + Theme.S(2), r.Width, sh), TextFormatFlags.EndEllipsis | al);
                        }
                    }
                    Rectangle chev = new Rectangle(Width - Pad - Theme.S(20), y, Theme.S(20), rh);
                    if (grp)
                        Theme.Glyph(g, IsOpen(i) ? "" : "", Theme.FontIconSmall, i == hover ? Theme.Text : Theme.Text2, chev);
                    else if (Clickable)
                        Theme.Glyph(g, "", Theme.FontIconSmall, i == hover ? Theme.Text : Theme.Text3, chev);
                }
                if (FooterH > 0)
                {
                    int fy = HeadH + vis.Count * rh;
                    g.DrawLine(sep, 1, fy, Width - 2, fy);
                    Theme.DrawText(g, Footer, Theme.FontSmall, Theme.Text3, new Rectangle(Pad, fy + Theme.S(13), Width - Pad * 2, FooterH - Theme.S(13)), TextFormatFlags.WordBreak);
                }
            }
            g.ResetClip(); clip.Dispose();
            Theme.DrawRound(g, Theme.CardEdge, card, Theme.Sf(14), 1f);
        }
    }
}
