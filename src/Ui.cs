using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Tunex
{
    class BufferedControl : Control
    {
        public BufferedControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            ForeColor = Theme.Text;
        }
    }

    // ---------------------------------------------------------------- Переключатель
    class ToggleSwitch : BufferedControl
    {
        bool isOn; float pos; readonly Timer anim = new Timer();
        public event EventHandler CheckedChanged;

        public ToggleSwitch()
        {
            Size = new Size(Theme.S(44), Theme.S(24));
            Cursor = Cursors.Hand;
            anim.Interval = 15;
            anim.Tick += delegate
            {
                float t = isOn ? 1f : 0f;
                pos += (t - pos) * 0.35f;
                if (Math.Abs(t - pos) < 0.02f) { pos = t; anim.Stop(); }
                Invalidate();
            };
        }

        public bool Checked
        {
            get { return isOn; }
            set { if (isOn == value) return; isOn = value; pos = value ? 1f : 0f; Invalidate(); }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (!Enabled) return;
            isOn = !isOn; anim.Start();
            if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
        }

        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color track = Theme.Blend(Theme.ToggleOff, Theme.Accent, pos);
            if (!Enabled) track = Theme.Blend(track, Theme.CardInner, 0.6f);
            Theme.FillRound(g, track, r, r.Height / 2);
            float d = Height - Theme.Sf(8);
            float x = Theme.Sf(4) + pos * (Width - Theme.Sf(8) - d);
            Color knob = !Enabled ? Color.FromArgb(140, 139, 132) : Theme.Blend(Color.FromArgb(236, 235, 230), Theme.OnAccent, pos);
            Theme.FillRound(g, knob, new RectangleF(x, Theme.Sf(4) - 0.5f, d, d), Theme.Sf(2));
        }

        protected override void Dispose(bool disposing) { if (disposing) anim.Dispose(); base.Dispose(disposing); }
    }

    // ---------------------------------------------------------------- Кнопки
    class FlatButton : BufferedControl
    {
        public bool Primary;
        public string Glyph;
        bool hover, down;

        public FlatButton(string text, bool primary) : this(text, primary, null) { }

        public FlatButton(string text, bool primary, string glyph)
        {
            Font = Theme.FontBodyBold;
            Primary = primary; Glyph = glyph;
            Cursor = Cursors.Hand;
            Height = Theme.S(34);
            Text = text;
        }

        public void FitWidth()
        {
            int w = Theme.Measure(Text, Font).Width + Theme.S(30);
            if (Glyph != null) w += Theme.S(24);
            Width = w;
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); FitWidth(); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; down = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); down = true; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); down = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            Color bg = Primary ? Theme.Accent : Theme.Button;
            if (!Enabled) bg = Theme.Blend(bg, Theme.Card, 0.55f);
            else if (down) bg = Theme.Blend(bg, Color.Black, 0.15f);
            else if (hover) bg = Primary ? Theme.Blend(bg, Color.White, 0.18f) : Theme.CardHover;
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRound(g, bg, r, r.Height / 2);
            if (!Primary) Theme.DrawRound(g, hover && Enabled ? Theme.Text3 : Theme.ButtonBorder, r, r.Height / 2, 1f);
            Color fg = !Enabled ? Theme.Text3 : Primary ? Theme.OnAccent : Theme.Text;
            if (Glyph != null)
            {
                int tw = Theme.Measure(Text, Font).Width;
                int total = tw + Theme.S(24);
                int x = (Width - total) / 2;
                Theme.Glyph(g, Glyph, Theme.FontIcon, fg, new Rectangle(x, 0, Theme.S(18), Height));
                Theme.DrawText(g, Text, Font, fg, new Rectangle(x + Theme.S(24), 0, tw + 4, Height), TextFormatFlags.VerticalCenter);
            }
            else
                Theme.DrawText(g, Text, Font, fg, ClientRectangle, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    class IconButton : BufferedControl
    {
        public string Glyph; public Color HoverColor = Color.FromArgb(28, 255, 255, 255);
        public Color GlyphColor = Theme.Text2;
        bool hover;
        public IconButton(string glyph, int size)
        {
            Glyph = glyph; Size = new Size(size, size); Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            if (hover) Theme.FillRound(g, HoverColor, new RectangleF(0, 0, Width - 1, Height - 1), Theme.Sf(8));
            Theme.Glyph(g, Glyph, Theme.FontIconSmall, hover ? Theme.Text : GlyphColor, ClientRectangle);
        }
    }

    // ---------------------------------------------------------------- Метка-«пилюля»
    class Pill : BufferedControl
    {
        public Color Tint;
        public Pill(string text, Color tint) { Font = Theme.FontSmallBold; Tint = tint; Text = text; }
        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Size s = Theme.Measure(Text, Font);
            Size = new Size(s.Width + Theme.S(32), s.Height + Theme.S(10));
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRound(g, Theme.Alpha(Tint, 22), r, 2);
            Theme.DrawRound(g, Theme.Alpha(Tint, 110), r, 2, 1f);
            float d = Theme.Sf(6);
            using (SolidBrush b = new SolidBrush(Tint)) g.FillRectangle(b, Theme.Sf(10), (Height - d) / 2f, d, d);
            Theme.DrawText(g, Text, Font, Tint, new Rectangle(Theme.S(22), 0, Width - Theme.S(28), Height), TextFormatFlags.VerticalCenter);
        }
    }

    // ---------------------------------------------------------------- Ползунок
    class DarkSlider : BufferedControl
    {
        public int Minimum = 0, Maximum = 100;
        int value; bool drag;
        public Func<int, string> Format;
        public event EventHandler ValueChanged;

        public DarkSlider()
        {
            Size = new Size(Theme.S(280), Theme.S(30)); Cursor = Cursors.Hand;
        }

        public int Value
        {
            get { return value; }
            set { int v = Math.Max(Minimum, Math.Min(Maximum, value)); if (v == this.value) return; this.value = v; Invalidate(); }
        }

        Rectangle Track { get { return new Rectangle(Theme.S(10), Height / 2 - Theme.S(2), Width - Theme.S(80), Theme.S(4)); } }

        void SetFromX(int x)
        {
            Rectangle t = Track;
            float f = (x - t.Left) / (float)Math.Max(1, t.Width);
            f = Math.Max(0, Math.Min(1, f));
            int v = Minimum + (int)Math.Round(f * (Maximum - Minimum));
            if (v != value) { value = v; Invalidate(); if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); }
        }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (!Enabled) return; drag = true; SetFromX(e.X); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (drag) SetFromX(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); drag = false; }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!Enabled) return;
            int v = Math.Max(Minimum, Math.Min(Maximum, value + (e.Delta > 0 ? 1 : -1)));
            if (v != value) { value = v; Invalidate(); if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            Rectangle t = Track;
            float f = (value - Minimum) / (float)Math.Max(1, Maximum - Minimum);
            Theme.FillRound(g, Theme.ToggleOff, t, t.Height / 2f);
            RectangleF fill = new RectangleF(t.X, t.Y, t.Width * f, t.Height);
            if (fill.Width > 1) Theme.FillRound(g, Enabled ? Theme.Accent : Theme.Text3, fill, t.Height / 2f);
            float kw = Theme.Sf(6), kh = Theme.Sf(18);
            float cx = t.X + t.Width * f;
            using (SolidBrush b = new SolidBrush(Enabled ? Theme.Text : Theme.Text3))
                g.FillRectangle(b, cx - kw / 2, Height / 2f - kh / 2, kw, kh);
            string s = Format != null ? Format(value) : value.ToString();
            Theme.DrawText(g, s, Theme.FontNum, Enabled ? Theme.Text : Theme.Text3,
                new Rectangle(Width - Theme.S(62), 0, Theme.S(62), Height), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
    }

    // ---------------------------------------------------------------- Выпадающий список
    class DarkColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected { get { return Theme.CardHover; } }
        public override Color MenuItemBorder { get { return Theme.CardHover; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color ToolStripDropDownBackground { get { return Theme.CardInner; } }
        public override Color ImageMarginGradientBegin { get { return Theme.CardInner; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.CardInner; } }
        public override Color ImageMarginGradientEnd { get { return Theme.CardInner; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.CardHover; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.CardHover; } }
        public override Color MenuItemPressedGradientBegin { get { return Theme.CardHover; } }
        public override Color MenuItemPressedGradientEnd { get { return Theme.CardHover; } }
    }

    class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = Theme.Text; base.OnRenderItemText(e); }
    }

    class DarkDropdown : BufferedControl
    {
        public readonly List<string> Items = new List<string>();
        int selected = -1; bool hover;
        public event EventHandler SelectedIndexChanged;

        public DarkDropdown()
        {
            Size = new Size(Theme.S(190), Theme.S(34)); Cursor = Cursors.Hand; Font = Theme.FontBodyBold;
        }

        public int SelectedIndex { get { return selected; } set { selected = value; Invalidate(); } }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (!Enabled || Items.Count == 0) return;
            ContextMenuStrip m = new ContextMenuStrip();
            m.Renderer = new DarkRenderer();
            m.ShowImageMargin = false;
            m.Font = Theme.FontBody;
            m.BackColor = Theme.CardInner;
            for (int i = 0; i < Items.Count; i++)
            {
                int idx = i;
                ToolStripItem it = m.Items.Add(Items[i]);
                it.ForeColor = Theme.Text;
                it.Padding = new Padding(Theme.S(6), Theme.S(5), Theme.S(6), Theme.S(5));
                if (i == selected) it.Font = Theme.FontBodyBold;
                it.AutoSize = true;
                it.Click += delegate
                {
                    if (idx == selected) return;
                    SelectedIndex = idx;
                    if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
                };
            }
            m.MinimumSize = new Size(Width, 0);
            m.Closed += delegate { BeginInvoke(new Action(m.Dispose)); };
            m.Show(this, new Point(0, Height + Theme.S(4)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRound(g, hover && Enabled ? Theme.CardHover : Theme.Button, r, Theme.Sf(10));
            Theme.DrawRound(g, hover && Enabled ? Theme.Text3 : Theme.ButtonBorder, r, Theme.Sf(10), 1f);
            string s = selected >= 0 && selected < Items.Count ? Items[selected] : "—";
            Theme.DrawText(g, s, Font, Enabled ? Theme.Text : Theme.Text3, new Rectangle(Theme.S(14), 0, Width - Theme.S(44), Height), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            Theme.Glyph(g, Icons.Chevron, Theme.FontIconSmall, Theme.Text2, new Rectangle(Width - Theme.S(34), 0, Theme.S(24), Height));
        }
    }

    // ---------------------------------------------------------------- Строки и карточки
    class Badge
    {
        public string Text; public Color Color;
        public Badge(string text, Color color) { Text = text; Color = color; }
    }

    class RowBase : BufferedControl
    {
        public bool RoundTop, RoundBottom, Hidden;
        public virtual int MeasureHeight(int width) { return Height; }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
        }
    }

    class SettingRow : RowBase
    {
        public string Title, Description;
        public Color DescriptionColor = Theme.Text2;
        public Control Accessory;
        public readonly List<Badge> Badges = new List<Badge>();
        public string Glyph; public Color GlyphColor = Theme.Accent;
        public Font TitleFont = Theme.FontBodyBold;
        bool modified;

        public SettingRow(string title, string description, Control right)
        {
            Title = title; Description = description; Accessory = right;
            if (right != null) Controls.Add(right);
        }

        public bool Modified { get { return modified; } set { if (modified == value) return; modified = value; Invalidate(); } }

        static int Pad { get { return Theme.S(18); } }
        int TextLeft { get { return Glyph != null ? Pad + Theme.S(30) : Pad; } }

        int TextWidth(int width)
        {
            int rw = Accessory != null ? Accessory.Width + Pad : 0;
            return Math.Max(Theme.S(80), width - TextLeft - Pad - rw);
        }

        public override int MeasureHeight(int width)
        {
            int tw = TextWidth(width);
            int h = Pad + Theme.Measure(Title, TitleFont, tw).Height;
            if (!string.IsNullOrEmpty(Description)) h += Theme.S(4) + Theme.Measure(Description, Theme.FontSmall, tw).Height;
            h += Pad;
            int min = Theme.S(56);
            if (Accessory != null) min = Math.Max(min, Accessory.Height + Theme.S(24));
            return Math.Max(h, min);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Accessory != null) Accessory.Location = new Point(Width - Pad - Accessory.Width, (Height - Accessory.Height) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            int tw = TextWidth(Width);
            int th = Theme.Measure(Title, TitleFont, tw).Height;
            int dh = string.IsNullOrEmpty(Description) ? 0 : Theme.S(3) + Theme.Measure(Description, Theme.FontSmall, tw).Height;
            int y = (Height - th - dh) / 2;

            if (modified)
                Theme.FillRound(g, Theme.Accent, new RectangleF(Theme.Sf(5), Theme.Sf(12), Theme.Sf(3), Height - Theme.Sf(24)), Theme.Sf(1.5f));

            if (Glyph != null)
            {
                Rectangle box = new Rectangle(Pad - Theme.S(2), (Height - Theme.S(24)) / 2, Theme.S(22), Theme.S(24));
                Theme.Glyph(g, Glyph, Theme.FontIcon, GlyphColor, box);
            }

            Theme.DrawText(g, Title, TitleFont, Enabled ? Theme.Text : Theme.Text3, new Rectangle(TextLeft, y, tw, th), TextFormatFlags.WordBreak);

            int bx = TextLeft + Math.Min(tw, Theme.Measure(Title, TitleFont).Width) + Theme.S(8);
            foreach (Badge b in Badges)
            {
                Size bs = Theme.Measure(b.Text, Theme.FontSmallBold);
                int bw = bs.Width + Theme.S(14), bh = bs.Height + Theme.S(4);
                if (bx + bw > TextLeft + tw) break;
                Rectangle br = new Rectangle(bx, y + (Theme.Measure("Ag", TitleFont).Height - bh) / 2, bw, bh);
                Theme.FillRound(g, Theme.Alpha(b.Color, 40), br, bh / 2f);
                Theme.DrawText(g, b.Text, Theme.FontSmallBold, b.Color, br, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                bx += bw + Theme.S(6);
            }

            if (dh > 0)
                Theme.DrawText(g, Description, Theme.FontSmall, DescriptionColor, new Rectangle(TextLeft, y + th + Theme.S(3), tw, dh), TextFormatFlags.WordBreak);
        }
    }

    class Card : BufferedControl
    {
        readonly List<RowBase> rows = new List<RowBase>();
        public string Footer;
        public Color BorderColor = Theme.Border;
        int footerTop = -1, lastWidth = -1;

        public Card() { }

        public static int Outer { get { return 0; } }
        readonly List<int> separators = new List<int>();

        public T Add<T>(T row) where T : RowBase
        {
            rows.Add(row); Controls.Add(row); Relayout(); return row;
        }

        public void Clear()
        {
            foreach (RowBase r in rows) { Controls.Remove(r); r.Dispose(); }
            rows.Clear(); Relayout();
        }

        public IList<RowBase> Rows { get { return rows; } }

        public void Relayout()
        {
            if (Width <= 0) return;
            int y = Outer, w = Width - Outer * 2;
            separators.Clear();
            List<RowBase> vis = new List<RowBase>();
            foreach (RowBase r in rows) if (!r.Hidden) vis.Add(r);
            for (int i = 0; i < vis.Count; i++)
            {
                RowBase r = vis[i];
                r.RoundTop = i == 0; r.RoundBottom = i == vis.Count - 1;
                int h = r.MeasureHeight(w);
                r.SetBounds(Outer, y, w, h);
                r.Invalidate();
                y += h;
                if (i < vis.Count - 1) { separators.Add(y); y += 1; }
            }
            footerTop = -1;
            if (!string.IsNullOrEmpty(Footer))
            {
                separators.Add(y);
                footerTop = y + Theme.S(14);
                y = footerTop + Theme.Measure(Footer, Theme.FontSmall, w - Theme.S(48)).Height + Theme.S(14);
            }
            y += Outer;
            if (y < Theme.S(20)) y = Theme.S(20);
            if (Height != y) Height = y;
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width != lastWidth) { lastWidth = Width; Relayout(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRound(g, Theme.Card, r, Theme.Sf(14));
            Theme.DrawRound(g, BorderColor, r, Theme.Sf(14), 1f);
            using (Pen sep = new Pen(Theme.Border))
                foreach (int y in separators) g.DrawLine(sep, 1, y, Width - 2, y);
            if (footerTop >= 0)
                Theme.DrawText(g, Footer, Theme.FontSmall, Theme.Text3,
                    new Rectangle(Theme.S(24), footerTop, Width - Theme.S(48), Height - footerTop), TextFormatFlags.WordBreak);
        }
    }

    class SectionLabel : BufferedControl
    {
        public SectionLabel(string text) { Text = text; Font = Theme.FontH2; Height = Theme.S(30); }
        protected override void OnPaint(PaintEventArgs e)
        {
            string t = Text.ToUpperInvariant();
            Size s = Theme.Measure(t, Font);
            int y = Height - Theme.S(6) - s.Height;
            Theme.DrawText(e.Graphics, t, Font, Theme.Text2, new Rectangle(0, y, s.Width + 4, s.Height), TextFormatFlags.Left);
            using (Pen p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, s.Width + Theme.S(14), y + s.Height / 2, Width, y + s.Height / 2);
        }
    }

    // Заголовок раздела: «01  ЭЛЕКТРОПИТАНИЕ  пояснение ———————»
    class SectionHeading : BufferedControl
    {
        public string Glyph, Title, Subtitle, Number;
        public SectionHeading(string glyph, string title, string subtitle)
        {
            Glyph = glyph; Title = title; Subtitle = subtitle; Height = Theme.S(36);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            int x = 0;
            if (Number != null)
            {
                Size ns = Theme.Measure(Number, Theme.FontH2);
                Theme.DrawText(g, Number, Theme.FontH2, Theme.Accent, new Rectangle(x, 0, ns.Width + Theme.S(4), Height), TextFormatFlags.VerticalCenter);
                x += ns.Width + Theme.S(12);
            }
            string t = Title.ToUpperInvariant();
            Size ts = Theme.Measure(t, Theme.FontH2);
            Theme.DrawText(g, t, Theme.FontH2, Theme.Text, new Rectangle(x, 0, ts.Width + Theme.S(4), Height), TextFormatFlags.VerticalCenter);
            x += ts.Width + Theme.S(16);
            if (!string.IsNullOrEmpty(Subtitle))
            {
                Size ss = Theme.Measure(Subtitle, Theme.FontSmall);
                int sw = Math.Min(ss.Width + Theme.S(4), Math.Max(0, Width - x - Theme.S(40)));
                Theme.DrawText(g, Subtitle, Theme.FontSmall, Theme.Text3, new Rectangle(x, 0, sw, Height), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                x += sw + Theme.S(16);
            }
            // Линия до правого края
            if (x < Width)
                using (Pen p = new Pen(Theme.Border)) g.DrawLine(p, x, Height / 2, Width, Height / 2);
        }
    }

    // Быстрые переходы: сплошная полоса с делениями, как переключатель на приборе
    class JumpBar : BufferedControl
    {
        class Item { public string Key, Glyph, Text; public Rectangle Bounds; }
        readonly List<Item> items = new List<Item>();
        int hover = -1;
        public event Action<string> Jump;
        public string Selected;   // для вкладок: выбранный пункт подсвечен всегда

        public JumpBar() { Height = Theme.S(34); Cursor = Cursors.Hand; }

        public void Add(string key, string glyph, string text)
        {
            Item it = new Item(); it.Key = key; it.Glyph = glyph; it.Text = text;
            items.Add(it); Relayout();
        }

        string Label(int i) { return (i + 1).ToString("00") + "  " + items[i].Text.ToUpperInvariant(); }

        void Relayout()
        {
            int x = 0;
            for (int i = 0; i < items.Count; i++)
            {
                int w = Theme.S(32) + Theme.Measure(Label(i), Theme.FontLabel).Width;
                items[i].Bounds = new Rectangle(x, 0, w, Height);
                x += w;
            }
            Invalidate();
        }

        int At(Point p) { for (int i = 0; i < items.Count; i++) if (items[i].Bounds.Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = At(e.Location); if (h != hover) { hover = h; Invalidate(); } Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }
        protected override void OnMouseClick(MouseEventArgs e) { base.OnMouseClick(e); int i = At(e.Location); if (i >= 0 && Jump != null) Jump(items[i].Key); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            if (items.Count == 0) return;
            int total = items[items.Count - 1].Bounds.Right;
            RectangleF strip = new RectangleF(0.5f, 0.5f, total - 1, Height - 1.5f);
            Theme.FillRound(g, Theme.Card, strip, 2);
            using (Pen p = new Pen(Theme.Border))
            {
                for (int i = 0; i < items.Count; i++)
                {
                    Item it = items[i];
                    bool on = i == hover || (Selected != null && it.Key == Selected);
                    if (on)
                    {
                        using (SolidBrush hb = new SolidBrush(Theme.CardHover)) g.FillRectangle(hb, it.Bounds.X + 1, 1, it.Bounds.Width - 1, Height - 2);
                        using (SolidBrush ab = new SolidBrush(Theme.Accent)) g.FillRectangle(ab, it.Bounds.X + 1, Height - Theme.S(3), it.Bounds.Width - 1, Theme.S(2));
                    }
                    if (i > 0) g.DrawLine(p, it.Bounds.X, Theme.S(8), it.Bounds.X, Height - Theme.S(8));
                    string num = (i + 1).ToString("00");
                    int nw = Theme.Measure(num + "  ", Theme.FontLabel).Width;
                    Theme.DrawText(g, num, Theme.FontLabel, on ? Theme.Accent : Theme.Text3, new Rectangle(it.Bounds.X + Theme.S(16), 0, nw, Height), TextFormatFlags.VerticalCenter);
                    Theme.DrawText(g, it.Text.ToUpperInvariant(), Theme.FontLabel, on ? Theme.Text : Theme.Text2,
                        new Rectangle(it.Bounds.X + Theme.S(16) + nw, 0, it.Bounds.Width - nw - Theme.S(16), Height), TextFormatFlags.VerticalCenter);
                }
            }
            Theme.DrawRound(g, Theme.Border, strip, 2, 1f);
        }
    }

    // Заголовок страницы: крупное название и линия с лаймовой меткой снизу
    class PageHeader : BufferedControl
    {
        public string Title, Subtitle, Glyph;
        public Control Accessory;
        public PageHeader(string glyph, string title, string subtitle, Control right)
        {
            Glyph = glyph; Title = title; Subtitle = subtitle; Accessory = right;
            if (right != null) Controls.Add(right);
            Height = Theme.S(60);
        }

        int TextWidth(int w) { return Math.Max(Theme.S(100), w - (Accessory != null ? Accessory.Width + Theme.S(24) : 0)); }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            int th = Theme.Measure(Title, Theme.FontTitle).Height;
            int h = th + Theme.S(14);
            if (!string.IsNullOrEmpty(Subtitle)) h += Theme.S(4) + Theme.Measure(Subtitle, Theme.FontBody, TextWidth(Width)).Height;
            h += Theme.S(4);
            if (h != Height) Height = h;
            if (Accessory != null) Accessory.Location = new Point(Width - Accessory.Width, Math.Max(0, (th - Accessory.Height) / 2));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            int th = Theme.Measure(Title, Theme.FontTitle).Height;
            Theme.DrawText(g, Title, Theme.FontTitle, Theme.Text, new Rectangle(-Theme.S(1), 0, TextWidth(Width), th), TextFormatFlags.Left);
            int y = th + Theme.S(4);
            if (!string.IsNullOrEmpty(Subtitle))
            {
                int sh = Theme.Measure(Subtitle, Theme.FontBody, TextWidth(Width)).Height;
                Theme.DrawText(g, Subtitle, Theme.FontBody, Theme.Text2, new Rectangle(0, y, TextWidth(Width), sh), TextFormatFlags.WordBreak);
                y += sh + Theme.S(4);
            }
            y = Height - Theme.S(4);
            using (Pen p = new Pen(Theme.Border)) g.DrawLine(p, 0, y, Width, y);
            using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, 0, y - Theme.S(1), Theme.S(36), Theme.S(3));
        }
    }

    // ---------------------------------------------------------------- Страница (вертикальный стек с прокруткой)
    class Page : FlowLayoutPanel
    {
        public const int MaxContentWidth = 1500;

        public Page()
        {
            FlowDirection = FlowDirection.TopDown;
            WrapContents = false;
            AutoScroll = true;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            Padding = new Padding(Theme.S(26), Theme.S(20), Theme.S(22), Theme.S(10));
            spacer = new Control(); spacer.Height = Theme.S(40); spacer.Margin = new Padding(0); spacer.BackColor = Theme.Bg;
            Controls.Add(spacer);
        }

        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x02000000; return cp; } // WS_EX_COMPOSITED — без мерцания
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetWindowTheme(Handle, "DarkMode_Explorer", null);
        }

        // Пустой блок в самом конце: FlowLayoutPanel не учитывает нижний отступ при прокрутке, и последний блок «обрезался»
        Control spacer;

        public T Add<T>(T c) where T : Control
        {
            c.Margin = new Padding(0, 0, 0, Theme.S(12));
            Controls.Add(c);
            if (spacer != null) Controls.SetChildIndex(spacer, Controls.Count - 1);
            FitWidths();
            return c;
        }

        public void Clear()
        {
            SuspendLayout();
            while (Controls.Count > 0) { Control c = Controls[0]; Controls.RemoveAt(0); if (c != spacer) c.Dispose(); }
            if (spacer != null) Controls.Add(spacer);
            ResumeLayout();
        }

        public int ContentWidth { get { return Math.Min(Theme.S(MaxContentWidth), ClientSize.Width - Padding.Horizontal); } }

        public void FitWidths()
        {
            int w = ContentWidth;
            if (w <= 0) return;
            foreach (Control c in Controls) if (c.Width != w) c.Width = w;
        }

        protected override void OnClientSizeChanged(EventArgs e) { base.OnClientSizeChanged(e); FitWidths(); }

        // Не прыгать к элементу с фокусом при переключении страниц
        protected override Point ScrollToControl(Control activeControl) { return DisplayRectangle.Location; }

        public virtual void OnShow() { }
        public virtual void OnTick() { }
    }

    // ---------------------------------------------------------------- Плитка мониторинга
    class StatTile : BufferedControl
    {
        public string Title, Glyph, Value = "—", Sub = "";
        public float[] Series; public float SeriesMax = 100;
        public Color LineColor = Theme.Accent;
        public List<float> Markers = new List<float>(); // 0..1 по оси X

        public StatTile(string glyph, string title, Color color)
        {
            Glyph = glyph; Title = title; LineColor = color; Height = Theme.S(138);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRound(g, Theme.Card, r, Theme.Sf(18));
            Theme.DrawRound(g, Theme.Border, r, Theme.Sf(18), 1f);
            int p = Theme.S(14);
            using (SolidBrush lb = new SolidBrush(LineColor)) g.FillRectangle(lb, p, p + Theme.S(8), Theme.S(6), Theme.S(6));
            Theme.DrawText(g, Theme.Track(Title), Theme.FontLabel, Theme.Text2, new Rectangle(p + Theme.S(14), p, Width - p * 2 - Theme.S(30), Theme.S(22)), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            Theme.Glyph(g, Glyph, Theme.FontIconSmall, Theme.Text3, new Rectangle(Width - p - Theme.S(18), p, Theme.S(18), Theme.S(22)));
            int vy = p + Theme.S(28);
            Theme.DrawText(g, Value, Theme.FontBig, Theme.Text, new Rectangle(p, vy, Width - p * 2, Theme.S(32)), TextFormatFlags.EndEllipsis);
            Theme.DrawText(g, Sub, Theme.FontSmall, Theme.Text2, new Rectangle(p, vy + Theme.S(31), Width - p * 2, Theme.S(18)), TextFormatFlags.EndEllipsis);

            Rectangle chart = new Rectangle(p, Height - Theme.S(40), Width - p * 2, Theme.S(28));
            if (Series != null && Series.Length > 1)
            {
                PointF[] pts = new PointF[Series.Length];
                float max = Math.Max(1f, SeriesMax);
                for (int i = 0; i < Series.Length; i++)
                {
                    float v = Series[i] < 0 ? 0 : Math.Min(Series[i], max);
                    pts[i] = new PointF(chart.X + chart.Width * i / (float)(Series.Length - 1), chart.Bottom - chart.Height * v / max);
                }
                using (GraphicsPath area = new GraphicsPath())
                {
                    area.AddLines(pts);
                    area.AddLine(pts[pts.Length - 1], new PointF(chart.Right, chart.Bottom));
                    area.AddLine(new PointF(chart.Right, chart.Bottom), new PointF(chart.X, chart.Bottom));
                    area.CloseFigure();
                    using (LinearGradientBrush lb = new LinearGradientBrush(new Rectangle(chart.X, chart.Y - 1, chart.Width, chart.Height + 2), Theme.Alpha(LineColor, 40), Theme.Alpha(LineColor, 0), 90f))
                        g.FillPath(lb, area);
                }
                using (Pen grid = new Pen(Theme.Border)) g.DrawLine(grid, chart.X, chart.Bottom, chart.Right, chart.Bottom);
                using (Pen pen = new Pen(LineColor, Theme.Sf(1.5f)))
                {
                    pen.LineJoin = LineJoin.Miter;
                    g.DrawLines(pen, pts);
                }
            }
            using (Pen mp = new Pen(Theme.Alpha(Theme.Orange, 200), Theme.Sf(1.5f)))
            {
                mp.DashStyle = DashStyle.Dash;
                foreach (float m in Markers)
                {
                    float x = chart.X + chart.Width * m;
                    g.DrawLine(mp, x, chart.Y - Theme.S(6), x, chart.Bottom);
                }
            }
        }
    }

    class TileRow : BufferedControl
    {
        public readonly List<Control> Tiles = new List<Control>();
        public void AddTile(Control c) { Tiles.Add(c); Controls.Add(c); Relayout(); }

        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Relayout(); }

        void Relayout()
        {
            if (Tiles.Count == 0 || Width <= 0) return;
            int gap = Theme.S(14);
            int cols = Width >= Theme.S(820) ? Tiles.Count : 2;
            int w = (Width - gap * (cols - 1)) / cols;
            int th = Tiles[0].Height;
            for (int i = 0; i < Tiles.Count; i++)
            {
                int c = i % cols, rr = i / cols;
                Tiles[i].SetBounds(c * (w + gap), rr * (th + gap), w, th);
            }
            int rowsN = (Tiles.Count + cols - 1) / cols;
            int h = rowsN * th + (rowsN - 1) * gap;
            if (Height != h) Height = h;
        }
    }

    // ---------------------------------------------------------------- Всплывающее уведомление
    class Toast : BufferedControl
    {
        readonly Timer hide = new Timer();
        public Color Tint = Theme.Accent;
        public Toast()
        {
            Font = Theme.FontBody; Visible = false;
            hide.Tick += delegate { hide.Stop(); Visible = false; };
        }

        public void ShowText(string text, Color tint, int ms)
        {
            Text = text; Tint = tint;
            Control host = Parent;
            if (host == null) return;
            int maxW = Math.Min(Theme.S(520), host.ClientSize.Width - Theme.S(40));
            Size s = Theme.Measure(text, Font, maxW - Theme.S(60));
            Size = new Size(Math.Min(maxW, s.Width + Theme.S(64)), s.Height + Theme.S(30));
            Location = new Point(host.ClientSize.Width - Width - Theme.S(24), host.ClientSize.Height - Height - Theme.S(24));
            Visible = true; BringToFront(); Invalidate();
            hide.Stop(); hide.Interval = ms; hide.Start();
        }

        protected override void OnClick(EventArgs e) { base.OnClick(e); Visible = false; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRound(g, Theme.CardInner, r, 2);
            Theme.DrawRound(g, Theme.Border, r, 2, 1f);
            using (SolidBrush b = new SolidBrush(Tint)) g.FillRectangle(b, 0, 0, Theme.S(3), Height);
            Theme.DrawText(g, Text, Font, Theme.Text, new Rectangle(Theme.S(22), Theme.S(15), Width - Theme.S(38), Height - Theme.S(26)), TextFormatFlags.WordBreak);
        }
    }

    // ---------------------------------------------------------------- Тёмный диалог
    class DarkDialog : Form
    {
        bool result;

        DarkDialog(string title, string text, string yes, string no)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Card;
            ShowInTaskbar = false;
            int w = Theme.S(460);
            Size ts = Theme.Measure(text, Theme.FontBody, w - Theme.S(56));
            Size = new Size(w, Theme.S(150) + ts.Height);
            Paint += delegate(object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics; Theme.Smooth(g);
                using (Pen pen = new Pen(Theme.Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                Theme.DrawText(g, title, Theme.FontH2, Theme.Text, new Rectangle(Theme.S(28), Theme.S(24), Width - Theme.S(56), Theme.S(30)), TextFormatFlags.Left);
                Theme.DrawText(g, text, Theme.FontBody, Theme.Text2, new Rectangle(Theme.S(28), Theme.S(60), Width - Theme.S(56), ts.Height + 4), TextFormatFlags.WordBreak);
            };
            FlatButton by = new FlatButton(yes, true);
            by.Click += delegate { result = true; Close(); };
            Controls.Add(by);
            by.Location = new Point(Width - Theme.S(28) - by.Width, Height - Theme.S(24) - by.Height);
            if (no != null)
            {
                FlatButton bn = new FlatButton(no, false);
                bn.Click += delegate { result = false; Close(); };
                Controls.Add(bn);
                bn.Location = new Point(by.Left - Theme.S(10) - bn.Width, by.Top);
            }
            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { result = false; Close(); } };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int v = 2; Native.DwmSetWindowAttribute(Handle, 33, ref v, 4);
        }

        public static bool Ask(IWin32Window owner, string title, string text, string yes, string no)
        {
            using (DarkDialog d = new DarkDialog(title, text, yes, no)) { d.ShowDialog(owner); return d.result; }
        }
    }
}
