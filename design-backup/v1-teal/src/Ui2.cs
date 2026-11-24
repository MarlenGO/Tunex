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

        public TweakCell(Tweak t) { Tweak = t; Cursor = Cursors.Hand; Sync(); }

        public void Sync() { state = TweakState.Of(Tweak); Invalidate(); }

        static int Pad { get { return Theme.S(20); } }
        int TextW(int w) { return Math.Max(Theme.S(80), w - Pad * 2); }

        public int Measure(int w)
        {
            int tw = TextW(w);
            int h = Pad + Theme.Measure(Tweak.Title, Theme.FontBodyBold, tw).Height + Theme.S(6) + Theme.Measure(Tweak.Description, Theme.FontSmall, tw).Height;
            h += Theme.S(8) + Theme.Measure("Ag", Theme.FontSmallBold).Height;
            return Math.Max(Theme.S(78), h + Pad);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
        protected override void OnClick(EventArgs e) { base.OnClick(e); App.Form.ShowTweak(Tweak, this); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            if (hover) using (SolidBrush b = new SolidBrush(Color.FromArgb(9, 255, 255, 255))) g.FillRectangle(b, ClientRectangle);
            int tw = TextW(Width);
            int th = Theme.Measure(Tweak.Title, Theme.FontBodyBold, tw).Height;
            int dh = Theme.Measure(Tweak.Description, Theme.FontSmall, tw).Height;
            int nh = Theme.S(8) + Theme.Measure("Ag", Theme.FontSmallBold).Height;
            int y = (Height - th - Theme.S(6) - dh - nh) / 2;
            bool dim = !state.Available;
            Theme.DrawText(g, Tweak.Title, Theme.FontBodyBold, dim ? Theme.Text3 : Theme.Text, new Rectangle(Pad, y, tw, th), TextFormatFlags.WordBreak);
            Theme.DrawText(g, Tweak.Description, Theme.FontSmall, dim ? Theme.Text3 : Theme.Text2, new Rectangle(Pad, y + th + Theme.S(6), tw, dh), TextFormatFlags.WordBreak);
            // Статус: значок + текст своим цветом
            int sy = y + th + Theme.S(6) + dh + Theme.S(8), sh = nh - Theme.S(8);
            Rectangle ico = new Rectangle(Pad - Theme.S(1), sy, Theme.S(14), sh);
            if (state.Available && state.On) Theme.Glyph(g, Icons.Check, Theme.FontIconSmall, state.Color, ico);
            else
            {
                float d = Theme.Sf(7);
                using (SolidBrush b = new SolidBrush(state.Color)) g.FillEllipse(b, ico.X + Theme.Sf(2), sy + (sh - d) / 2f, d, d);
            }
            Theme.DrawText(g, state.Status, Theme.FontSmallBold, state.Color, new Rectangle(Pad + Theme.S(16), sy, tw - Theme.S(16), sh), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
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
            Theme.FillRound(g, Theme.Card, r, Theme.Sf(14));
            Theme.DrawRound(g, Theme.Border, r, Theme.Sf(14), 1f);
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
            using (SolidBrush b = new SolidBrush(Color.FromArgb(backdrop != null ? 170 : 220, 6, 7, 9))) g.FillRectangle(b, ClientRectangle);
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
            Theme.FillRound(g, Theme.Card, r, Theme.Sf(16));
            Theme.DrawRound(g, Theme.Border, r, Theme.Sf(16), 1f);
            using (Pen p = new Pen(Theme.Border)) foreach (int y in Separators) g.DrawLine(p, 1, y, Width - 2, y);
            PaintContent(g);
        }

        protected virtual void PaintContent(Graphics g) { }

        protected static void Chip(Graphics g, string glyph, string text, Color color, ref int x, int y)
        {
            Size ts = Theme.Measure(text, Theme.FontSmall);
            int h = Theme.S(36), w = ts.Width + Theme.S(glyph != null ? 52 : 28);
            Rectangle r = new Rectangle(x, y, w, h);
            Theme.FillRound(g, Theme.CardInner, r, Theme.Sf(10));
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
                Theme.DrawText(g, stepText.ToUpperInvariant(), Theme.FontSmallBold, Theme.Accent, new Rectangle(Pad, top, w, Theme.S(20)), TextFormatFlags.Left);
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
            Theme.DrawText(g, tweak.Details ?? "", Theme.FontBody, Color.FromArgb(200, 203, 208), new Rectangle(Pad, y, w, dh), TextFormatFlags.WordBreak);
            y += dh + Theme.S(18);
            int x = Pad;
            foreach (KeyValuePair<string, Color> c in chips)
            {
                int cw = Theme.Measure(c.Key, Theme.FontSmall).Width + Theme.S(52);
                if (x > Pad && x + cw > Pad + w) { x = Pad; y += Theme.S(44); }
                Chip(g, c.Value == Theme.Text2 ? Icons.Info : c.Value == Theme.Purple ? Icons.Refresh : Icons.Warning, c.Key, c.Value, ref x, y);
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
            Theme.FillRound(g, Theme.Card, rf, Theme.Sf(14));
            Theme.DrawRound(g, Theme.Border, rf, Theme.Sf(14), 1f);
        }

        static void Cell(Graphics g, Rectangle r, string value, string label, Color valueColor)
        {
            int p = Theme.S(24);
            int vh = Theme.Measure("0", Theme.FontH2).Height;
            int lh = Theme.Measure("A", Theme.FontSmall).Height;
            int y = r.Y + (r.Height - vh - lh - Theme.S(4)) / 2;
            Theme.DrawText(g, value, Theme.FontH2, valueColor, new Rectangle(r.X + p, y, r.Width - p * 2, vh), TextFormatFlags.EndEllipsis);
            Theme.DrawText(g, label, Theme.FontSmall, Theme.Text2, new Rectangle(r.X + p, y + vh + Theme.S(4), r.Width - p * 2, lh + 2), TextFormatFlags.EndEllipsis);
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
            int pad = Theme.S(24);
            Theme.DrawText(g, bench != null ? bench.Total.ToString() : "—", Theme.FontBig, Theme.Text, new Rectangle(rate.X + pad, rate.Y + pad - Theme.S(4), rate.Width - pad, Theme.S(40)), TextFormatFlags.Left);
            Theme.DrawText(g, bench != null ? "Игровой рейтинг · " + Bench.Tier(bench.Total) : "Игровой рейтинг", Theme.FontSmall, Theme.Text2, new Rectangle(rate.X + pad, rate.Y + pad + Theme.S(38), rate.Width - pad, Theme.S(20)), TextFormatFlags.Left);
            DrawCurve(g, new Rectangle(rate.X + 1, rate.Y + rate.Height / 2, rate.Width - 2, rate.Height / 2 - 1));

            string cpu = bench != null ? bench.Cpu.ToString() : "—";
            string ram = bench != null ? bench.RamScore.ToString() : "—";
            string disk = bench != null && bench.DiskScore > 0 ? bench.DiskScore.ToString() : "—";
            string last = bench != null ? FormatDate(bench.When) : "Не было";
            Cell(g, new Rectangle(left.X + c0, left.Y, cw, rh), cpu, "Процессор", Theme.Text);
            Cell(g, new Rectangle(left.X + c0 + cw, left.Y, left.Width - c0 - cw, rh), bench != null ? bench.GpuScore + "  ·  " + Bench.GpuTier().ToLowerInvariant() : SysInfo.HardwareLoaded ? Bench.GpuTier() : "…", "Видеокарта", Theme.Text);
            Cell(g, new Rectangle(left.X + c0, left.Y + rh, cw, left.Height - rh), ram + "  /  " + disk, "Память  /  Диск", Theme.Text);
            Cell(g, new Rectangle(left.X + c0 + cw, left.Y + rh, left.Width - c0 - cw, left.Height - rh), last, "Последняя проверка", Theme.Text);

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

        void DrawCurve(Graphics g, Rectangle r)
        {
            int n = 60;
            PointF[] pts = new PointF[n + 1];
            for (int i = 0; i <= n; i++)
            {
                double x = i / (double)n;
                double yv = Math.Exp(-Math.Pow((x - 0.45) / 0.2, 2));
                pts[i] = new PointF(r.X + (float)(x * r.Width), r.Bottom - (float)(yv * r.Height * 0.85));
            }
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddLines(pts);
                p.AddLine(pts[n], new PointF(r.Right, r.Bottom));
                p.AddLine(new PointF(r.Right, r.Bottom), new PointF(r.X, r.Bottom));
                p.CloseFigure();
                Region old = g.Clip;
                using (GraphicsPath clip = Theme.RoundPath(new RectangleF(left.X, left.Y, left.Width, left.Height), Theme.Sf(14))) g.SetClip(clip);
                using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(bench != null ? 70 : 30, Theme.Accent), Color.FromArgb(0, Theme.Accent), 90f))
                    g.FillPath(b, p);
                g.Clip = old;
            }
            if (bench != null)
            {
                float fx = Math.Max(0.03f, Math.Min(0.97f, bench.Total / 2000f));
                double yv = Math.Exp(-Math.Pow((fx - 0.45) / 0.2, 2));
                float mx = r.X + fx * r.Width, my = r.Bottom - (float)(yv * r.Height * 0.85);
                using (Pen pen = new Pen(Theme.Alpha(Theme.Accent, 160), Theme.Sf(1.5f))) g.DrawLine(pen, mx, my, mx, r.Bottom);
                float d = Theme.Sf(10);
                using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, mx - d / 2, my - d / 2, d, d);
            }
        }

        static string FormatDate(DateTime d)
        {
            if (d.Date == DateTime.Today) return "Сегодня, " + d.ToString("HH:mm");
            if (d.Date == DateTime.Today.AddDays(-1)) return "Вчера, " + d.ToString("HH:mm");
            return d.ToString("d MMM", new CultureInfo("ru-RU"));
        }
    }
}
