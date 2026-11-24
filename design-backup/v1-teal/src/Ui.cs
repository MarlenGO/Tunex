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
            float d = Height - Theme.Sf(6);
            float x = Theme.Sf(3) + pos * (Width - Theme.Sf(6) - d);
            using (SolidBrush b = new SolidBrush(Enabled ? Color.FromArgb(240, 242, 244) : Color.FromArgb(150, 150, 155)))
                g.FillEllipse(b, x, Theme.Sf(3) - 0.5f, d, d);
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
            else if (hover) bg = Theme.Blend(bg, Color.White, 0.10f);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRound(g, bg, r, r.Height / 2);
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
            Size = new Size(s.Width + Theme.S(20), s.Height + Theme.S(10));
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRound(g, Theme.Alpha(Tint, 38), r, r.Height / 2);
            Theme.DrawText(g, Text, Font, Tint, ClientRectangle, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
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
            float d = Theme.Sf(18);
            float cx = t.X + t.Width * f;
            using (SolidBrush b = new SolidBrush(Enabled ? Color.White : Theme.Text3))
                g.FillEllipse(b, cx - d / 2, Height / 2f - d / 2, d, d);
            string s = Format != null ? Format(value) : value.ToString();
            Theme.DrawText(g, s, Theme.FontBodyBold, Enabled ? Theme.Text : Theme.Text3,
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
            Theme.DrawRound(g, Color.FromArgb(60, 63, 69), r, Theme.Sf(10), 1f);
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
        int TextLeft { get { return Glyph != null ? Pad + Theme.S(34) : Pad; } }

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
                Rectangle box = new Rectangle(Pad, (Height - Theme.S(28)) / 2, Theme.S(28), Theme.S(28));
                Theme.FillRound(g, Theme.Alpha(GlyphColor, 36), box, Theme.Sf(8));
                Theme.Glyph(g, Glyph, Theme.FontIconSmall, GlyphColor, box);
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
        public SectionLabel(string text) { Text = text; Font = Theme.FontBodyBold; Height = Theme.S(26); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.DrawText(e.Graphics, Text, Font, Color.FromArgb(196, 199, 204), new Rectangle(Theme.S(6), 0, Width, Height - Theme.S(6)), TextFormatFlags.Bottom);
        }
    }

    // Заголовок раздела на общей странице: значок + название + пояснение
    class SectionHeading : BufferedControl
    {
        public string Glyph, Title, Subtitle;
        public SectionHeading(string glyph, string title, string subtitle)
        {
            Glyph = glyph; Title = title; Subtitle = subtitle; Height = Theme.S(42);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            Rectangle box = new Rectangle(Theme.S(2), Theme.S(6), Theme.S(30), Theme.S(30));
            Theme.FillRound(g, Theme.AccentSoft, box, Theme.Sf(9));
            Theme.Glyph(g, Glyph, Theme.FontIcon, Theme.AccentText, box);
            int x = Theme.S(44);
            Size ts = Theme.Measure(Title, Theme.FontLogo);
            Theme.DrawText(g, Title, Theme.FontLogo, Theme.Text, new Rectangle(x, 0, ts.Width + Theme.S(4), Height), TextFormatFlags.VerticalCenter);
            if (!string.IsNullOrEmpty(Subtitle))
                Theme.DrawText(g, Subtitle, Theme.FontSmall, Theme.Text3, new Rectangle(x + ts.Width + Theme.S(14), 0, Width - x - ts.Width - Theme.S(14), Height),
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    // Быстрые переходы к разделам страницы
    class JumpBar : BufferedControl
    {
        class Item { public string Key, Glyph, Text; public Rectangle Bounds; }
        readonly List<Item> items = new List<Item>();
        int hover = -1;
        public event Action<string> Jump;

        public JumpBar() { Height = Theme.S(36); Cursor = Cursors.Hand; }

        public void Add(string key, string glyph, string text)
        {
            Item it = new Item(); it.Key = key; it.Glyph = glyph; it.Text = text;
            items.Add(it); Relayout();
        }

        void Relayout()
        {
            int x = 0;
            foreach (Item it in items)
            {
                int w = Theme.S(40) + Theme.Measure(it.Text, Theme.FontBody).Width;
                it.Bounds = new Rectangle(x, 0, w, Height);
                x += w + Theme.S(8);
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
            for (int i = 0; i < items.Count; i++)
            {
                Item it = items[i];
                RectangleF r = new RectangleF(it.Bounds.X + 0.5f, 0.5f, it.Bounds.Width - 1, Height - 1.5f);
                Theme.FillRound(g, i == hover ? Theme.CardHover : Theme.Card, r, r.Height / 2);
                Theme.DrawRound(g, Theme.Border, r, r.Height / 2, 1f);
                Theme.Glyph(g, it.Glyph, Theme.FontIcon, Theme.AccentText, new Rectangle(it.Bounds.X + Theme.S(12), 0, Theme.S(18), Height));
                Theme.DrawText(g, it.Text, Theme.FontBody, Theme.Text, new Rectangle(it.Bounds.X + Theme.S(34), 0, it.Bounds.Width - Theme.S(34), Height), TextFormatFlags.VerticalCenter);
            }
        }
    }

    class PageHeader : BufferedControl
    {
        public string Title, Subtitle, Glyph;
        public Control Accessory;
        public PageHeader(string glyph, string title, string subtitle, Control right)
        {
            Glyph = glyph; Title = title; Subtitle = subtitle; Accessory = right;
            if (right != null) Controls.Add(right);
            Height = Theme.S(76);
        }

        int TextLeft { get { return Theme.S(62); } }
        int TextWidth(int w) { return Math.Max(Theme.S(100), w - TextLeft - (Accessory != null ? Accessory.Width + Theme.S(24) : 0)); }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            int h = string.IsNullOrEmpty(Subtitle) ? Theme.S(50)
                : Math.Max(Theme.S(50), Theme.Measure(Title, Theme.FontTitle).Height + Theme.S(2) + Theme.Measure(Subtitle, Theme.FontBody, TextWidth(Width)).Height) + Theme.S(16);
            if (h != Height) Height = h;
            if (Accessory != null) Accessory.Location = new Point(Width - Accessory.Width, Theme.S(10));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            Rectangle box = new Rectangle(0, Theme.S(2), Theme.S(46), Theme.S(46));
            Theme.FillRound(g, Theme.CardInner, box, Theme.Sf(14));
            Theme.DrawRound(g, Theme.Border, box, Theme.Sf(14), 1f);
            Theme.Glyph(g, Glyph, Theme.FontIconBig, Theme.Accent, box);
            int th = Theme.Measure(Title, Theme.FontTitle).Height;
            if (string.IsNullOrEmpty(Subtitle))
            {
                // Только заголовок — по центру значка
                Theme.DrawText(g, Title, Theme.FontTitle, Theme.Text, new Rectangle(TextLeft, box.Y, TextWidth(Width), box.Height), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                return;
            }
            Theme.DrawText(g, Title, Theme.FontTitle, Theme.Text, new Rectangle(TextLeft, -Theme.S(4), TextWidth(Width), th), TextFormatFlags.Left);
            Theme.DrawText(g, Subtitle, Theme.FontBody, Theme.Text2, new Rectangle(TextLeft, th - Theme.S(2), TextWidth(Width), Height), TextFormatFlags.WordBreak);
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
            Rectangle ib = new Rectangle(p, p, Theme.S(24), Theme.S(24));
            Theme.FillRound(g, Theme.Alpha(LineColor, 36), ib, Theme.Sf(8));
            Theme.Glyph(g, Glyph, Theme.FontIconSmall, LineColor, ib);
            Theme.DrawText(g, Title, Theme.FontSmallBold, Theme.Text2, new Rectangle(p + Theme.S(32), p, Width - p * 2, Theme.S(24)), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            int vy = p + Theme.S(30);
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
                    using (LinearGradientBrush lb = new LinearGradientBrush(new Rectangle(chart.X, chart.Y - 1, chart.Width, chart.Height + 2), Theme.Alpha(LineColor, 70), Theme.Alpha(LineColor, 0), 90f))
                        g.FillPath(lb, area);
                }
                using (Pen pen = new Pen(LineColor, Theme.Sf(2f)))
                {
                    pen.LineJoin = LineJoin.Round;
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
            Theme.FillRound(g, Color.FromArgb(32, 35, 39), r, Theme.Sf(14));
            Theme.DrawRound(g, Theme.Alpha(Tint, 160), r, Theme.Sf(14), 1f);
            Theme.FillRound(g, Tint, new RectangleF(Theme.Sf(16), Height / 2f - Theme.Sf(4), Theme.Sf(8), Theme.Sf(8)), Theme.Sf(4));
            Theme.DrawText(g, Text, Font, Theme.Text, new Rectangle(Theme.S(36), Theme.S(15), Width - Theme.S(52), Height - Theme.S(26)), TextFormatFlags.WordBreak);
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
