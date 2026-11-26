using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace Tunex
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Theme.Init();
            SysInfo.InitFast();
            Catalog.Build();
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length >= 3 && args[1] == "--selftest") { SelfTest.Run(args[2]); return; }
            Updater.CleanupOld();
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                if (App.Form != null) App.Error("Ошибка: " + e.Exception.Message);
            };
            Application.Run(new MainForm());
        }
    }

    // ---------------------------------------------------------------- Кнопка «Бэкап · сегодня 14:32» в заголовке окна
    class BackupButton : BufferedControl
    {
        string label = "Бэкап";
        bool hover;

        public BackupButton() { Cursor = Cursors.Hand; Fit(); }

        public void SetTime(BackupFile b)
        {
            label = b == null ? "Бэкап" : "Бэкап · " + Backups.Short(b.When);
            Fit(); Invalidate();
        }

        void Fit() { Size = new Size(Theme.S(38) + Theme.Measure(label, Theme.FontSmallBold).Width, Theme.S(26)); }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRound(g, hover ? Theme.CardHover : Theme.Button, r, Theme.Sf(3));
            Theme.DrawRound(g, hover ? Theme.Text3 : Theme.ButtonBorder, r, Theme.Sf(3), 1f);
            Theme.Glyph(g, Icons.Save, Theme.FontIconSmall, Theme.Accent, new Rectangle(Theme.S(8), 0, Theme.S(18), Height));
            Theme.DrawText(g, label, Theme.FontSmallBold, Theme.Text, new Rectangle(Theme.S(30), 0, Width - Theme.S(34), Height), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    // ---------------------------------------------------------------- Заголовок окна
    class TitleBar : BufferedControl
    {
        readonly Form form;
        int hoverBtn = -1;
        public Pill AdminPill;
        public BackupButton Backup;

        public TitleBar(Form f)
        {
            form = f; Height = Theme.S(40);
            Backup = new BackupButton();
            Backup.Click += delegate { App.Form.Navigate("backup"); };
            Controls.Add(Backup);
            AdminPill = new Pill(SysInfo.IsAdmin ? "Администратор" : "Без прав администратора", SysInfo.IsAdmin ? Theme.Green : Theme.Orange);
            if (!SysInfo.IsAdmin) { AdminPill.Cursor = Cursors.Hand; AdminPill.Click += delegate { App.Form.RestartAsAdmin(); }; }
            Controls.Add(AdminPill);
        }

        // Маленькие кнопки окна: 28×24, с промежутком
        int BtnW { get { return Theme.S(28); } }
        int BtnGap { get { return Theme.S(6); } }
        Rectangle Btn(int i)
        {
            int h = Theme.S(24), right = Width - Theme.S(10);
            return new Rectangle(right - (BtnW + BtnGap) * (3 - i) + BtnGap, (Height - h) / 2, BtnW, h);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (AdminPill != null) AdminPill.Location = new Point(Btn(0).Left - AdminPill.Width - Theme.S(12), (Height - AdminPill.Height) / 2);
            if (Backup != null && AdminPill != null) Backup.Location = new Point(AdminPill.Left - Backup.Width - Theme.S(10), (Height - Backup.Height) / 2);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = -1;
            for (int i = 0; i < 3; i++) if (Btn(i).Contains(e.Location)) h = i;
            if (h != hoverBtn) { hoverBtn = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hoverBtn = -1; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            for (int i = 0; i < 3; i++) if (Btn(i).Contains(e.Location)) return;
            if (e.Clicks == 2) { ToggleMax(); return; }
            Native.ReleaseCapture();
            Native.SendMessage(form.Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            if (Btn(0).Contains(e.Location)) form.WindowState = FormWindowState.Minimized;
            else if (Btn(1).Contains(e.Location)) ToggleMax();
            else if (Btn(2).Contains(e.Location)) form.Close();
        }

        void ToggleMax()
        {
            ((MainForm)form).ToggleMaximize();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            // Логотип — тот же рисунок, что и в app.ico
            int x = Theme.S(20), ls = Theme.S(20);
            Logo.Draw(g, new RectangleF(x, (Height - ls) / 2f, ls, ls));
            Theme.Smooth(g);
            Theme.DrawText(g, Theme.Track(Theme.AppName), Theme.FontLogo, Theme.Text, new Rectangle(x + ls + Theme.S(10), 0, Theme.S(200), Height), TextFormatFlags.VerticalCenter);
            using (Pen bp = new Pen(Theme.Border)) g.DrawLine(bp, 0, Height - 1, Width, Height - 1);

            string[] glyphs = { Icons.Min, form.WindowState == FormWindowState.Maximized ? Icons.Restore : Icons.Max, Icons.Close };
            for (int i = 0; i < 3; i++)
            {
                Rectangle r = Btn(i);
                if (i == hoverBtn) Theme.FillRound(g, i == 2 ? Color.FromArgb(196, 43, 28) : Theme.CardHover, new RectangleF(r.X, r.Y, r.Width, r.Height), 2);
                Theme.Glyph(g, glyphs[i], Theme.FontIconTiny, i == hoverBtn ? Theme.Text : Theme.Text2, r);
            }
        }
    }

    // ---------------------------------------------------------------- Боковое меню
    class NavItem : BufferedControl
    {
        public string Key, Glyph; public bool Active; bool hover;
        public NavItem(string key, string glyph, string text)
        {
            Key = key; Glyph = glyph; Text = text; Height = Theme.S(48); Cursor = Cursors.Hand; Font = Theme.FontNav;
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); NavTip.ShowFor(this); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); NavTip.HideFor(this); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            int s = Theme.S(40);
            RectangleF r = new RectangleF((Width - s) / 2f, (Height - s) / 2f, s, s);
            if (hover && !Active) Theme.FillRound(g, Color.FromArgb(14, 255, 255, 255), r, 2);
            if (Active) using (SolidBrush ab = new SolidBrush(Theme.Accent)) g.FillRectangle(ab, 0, (Height - Theme.S(22)) / 2, Theme.S(3), Theme.S(22));
            Color c = Active ? Theme.Accent : hover ? Theme.Text : Theme.Text3;
            Theme.Glyph(g, Glyph, Theme.FontIconNav, c, Rectangle.Round(r));
        }
    }

    // Подсказка с названием раздела справа от значка меню
    static class NavTip
    {
        static ToolTip tip;
        static Control owner;

        static ToolTip Tip
        {
            get
            {
                if (tip != null) return tip;
                tip = new ToolTip();
                tip.OwnerDraw = true;
                tip.UseAnimation = false; tip.UseFading = false;
                tip.Popup += delegate(object s, PopupEventArgs e)
                {
                    Size t = Theme.Measure(tip.GetToolTip(e.AssociatedControl) ?? "", Theme.FontBodyBold);
                    if (owner != null) t = Theme.Measure(owner.Text, Theme.FontBodyBold);
                    e.ToolTipSize = new Size(t.Width + Theme.S(24), t.Height + Theme.S(14));
                };
                tip.Draw += delegate(object s, DrawToolTipEventArgs e)
                {
                    using (SolidBrush b = new SolidBrush(Theme.CardInner)) e.Graphics.FillRectangle(b, e.Bounds);
                    using (Pen p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                    TextRenderer.DrawText(e.Graphics, e.ToolTipText, Theme.FontBodyBold, e.Bounds, Theme.Text,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                };
                return tip;
            }
        }

        public static void ShowFor(Control c)
        {
            owner = c;
            int h = Theme.Measure(c.Text, Theme.FontBodyBold).Height + Theme.S(14);
            Tip.Show(c.Text, c, c.Width - Theme.S(4), (c.Height - h) / 2);
        }

        public static void HideFor(Control c) { if (tip != null) tip.Hide(c); if (owner == c) owner = null; }
    }

    class Sidebar : BufferedControl
    {
        public readonly List<NavItem> Items = new List<NavItem>();
        readonly List<int> separators = new List<int>();
        public event Action<string> Selected;
        int y = Theme.S(8);

        public Sidebar() { Width = Theme.S(64); BackColor = Theme.Sidebar; }

        public void AddItem(string key, string glyph, string text)
        {
            NavItem n = new NavItem(key, glyph, text);
            n.SetBounds(0, y, Width, n.Height);
            n.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            n.Click += delegate { if (Selected != null) Selected(n.Key); };
            Items.Add(n); Controls.Add(n);
            y += n.Height;
        }

        public void AddSeparator() { separators.Add(y + Theme.S(6)); y += Theme.S(12); }

        public void SetActive(string key) { foreach (NavItem n in Items) { n.Active = n.Key == key; n.Invalidate(); } }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Theme.Sidebar)) g.FillRectangle(b, ClientRectangle);
            using (GraphicsPath p = new GraphicsPath())
            {
                RectangleF glow = new RectangleF(-Width * 0.6f, -Theme.Sf(260), Width * 1.8f, Theme.Sf(560));
                p.AddEllipse(glow);
                using (PathGradientBrush pb = new PathGradientBrush(p))
                {
                    pb.CenterColor = Theme.Alpha(Theme.SidebarGlow, 200);
                    pb.SurroundColors = new Color[] { Theme.Alpha(Theme.Sidebar, 0) };
                    g.FillPath(pb, p);
                }
            }
            using (Pen pen = new Pen(Theme.Border)) g.DrawLine(pen, Width - 1, 0, Width - 1, Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (Pen pen = new Pen(Theme.Border))
                foreach (int s in separators) e.Graphics.DrawLine(pen, Theme.S(16), s, Width - Theme.S(16), s);
            Theme.DrawText(e.Graphics, "v" + Theme.Version, Theme.FontSmall, Theme.Text3,
                new Rectangle(0, Height - Theme.S(30), Width, Theme.S(20)), TextFormatFlags.HorizontalCenter);
        }
    }

    // ---------------------------------------------------------------- Панель «Применить»
    class PendingBar : BufferedControl
    {
        public readonly FlatButton ApplyBtn, CancelBtn;
        public string Info = "", Sub = "";

        public PendingBar()
        {
            Height = Theme.S(78); BackColor = Theme.Bg;
            ApplyBtn = new FlatButton("Применить", true, Icons.Check);
            CancelBtn = new FlatButton("Отменить", false);
            Controls.Add(ApplyBtn); Controls.Add(CancelBtn);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (ApplyBtn == null || CancelBtn == null) return;
            int m = Theme.S(20);
            ApplyBtn.Location = new Point(Width - m - Theme.S(16) - ApplyBtn.Width, (Height - ApplyBtn.Height) / 2);
            CancelBtn.Location = new Point(ApplyBtn.Left - Theme.S(10) - CancelBtn.Width, ApplyBtn.Top);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            RectangleF r = new RectangleF(Theme.Sf(20), Theme.Sf(8), Width - Theme.Sf(40), Height - Theme.Sf(16));
            Theme.FillRound(g, Theme.Card, r, Theme.Sf(16));
            Theme.DrawRound(g, Theme.Alpha(Theme.Accent, 140), r, Theme.Sf(16), 1f);
            int x = Theme.S(40);
            Theme.DrawText(g, Info, Theme.FontBodyBold, Theme.Text, new Rectangle(x, Theme.S(18), CancelBtn.Left - x, Theme.S(22)), TextFormatFlags.EndEllipsis);
            Theme.DrawText(g, Sub, Theme.FontSmall, Theme.Text2, new Rectangle(x, Theme.S(41), CancelBtn.Left - x, Theme.S(20)), TextFormatFlags.EndEllipsis);
        }
    }

    // ---------------------------------------------------------------- Главное окно
    class MainForm : Form
    {
        readonly TitleBar title;
        readonly Sidebar sidebar;
        readonly Panel content;
        readonly Toast toast;
        readonly ModalOverlay modal = new ModalOverlay();
        readonly Dictionary<string, Func<Page>> factories = new Dictionary<string, Func<Page>>();
        readonly Dictionary<string, Page> pages = new Dictionary<string, Page>();
        readonly SensorService sensors = new SensorService();
        readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer();
        Page current; string currentKey; int gameTick;

        public MainForm()
        {
            App.Form = this;
            App.Manager = new TweakManager(AppState.Load(), sensors);
            Color accent;
            if (Theme.TryHex(App.Manager.State.Accent, out accent)) Theme.SetAccent(accent);

            Text = Theme.AppName;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Theme.Border;
            Padding = new Padding(1);
            Size = new Size(Theme.S(1180), Theme.S(780));
            MinimumSize = new Size(Theme.S(860), Theme.S(560));
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            content = new Panel(); content.Dock = DockStyle.Fill; content.BackColor = Theme.Bg;
            sidebar = new Sidebar(); sidebar.Dock = DockStyle.Left;
            title = new TitleBar(this); title.Dock = DockStyle.Top; title.BackColor = Theme.Sidebar;
            Controls.Add(content); Controls.Add(sidebar); Controls.Add(title);

            toast = new Toast();
            content.Controls.Add(toast);
            Controls.Add(modal);
            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape && modal.IsOpen) CloseModal(); };
            modal.Closed += delegate { RefreshCurrent(); };
            Resize += delegate { if (modal.IsOpen) modal.Bounds = ClientRectangle; };

            factories["home"] = delegate { return new HomePage(); };
            factories["tune"] = delegate { return new OptimizePage(); };
            factories["pc"] = delegate { return new PcPage(); };
            factories["games"] = delegate { return new GamesPage(); };
            factories["display"] = delegate { return new DisplayPage(); };
            factories["nvidia"] = delegate { return new NvidiaPage(); };
            factories["journal"] = delegate { return new JournalPage(); };
            factories["look"] = delegate { return new LookPage(); };
            factories["backup"] = delegate { return new BackupPage(); };
            factories["settings"] = delegate { return new SettingsPage(); };

            sidebar.AddItem("home", Icons.Home, "Главная");
            sidebar.AddItem("tune", Icons.Speed, "Оптимизация");
            sidebar.AddItem("pc", Icons.Cpu, "Оценка ПК");
            sidebar.AddItem("games", Icons.Library, "Игры");
            sidebar.AddItem("display", Icons.Display, "Экран");
            sidebar.AddItem("nvidia", Icons.Chip, "NVIDIA");
            sidebar.AddSeparator();
            sidebar.AddItem("journal", Icons.History, "Журнал");
            sidebar.AddItem("look", Icons.Palette, "Оформление");
            sidebar.AddItem("settings", Icons.Settings, "Настройки");
            sidebar.Selected += Navigate;


            tick.Interval = 1000;
            tick.Tick += delegate
            {
                App.Manager.CheckObservations();
                if (current != null) current.OnTick();
                if (++gameTick % 3 == 0) { try { WatchGames(); } catch { } }
                if (gameTick % 15 == 0) UpdateBackupButton();
            };

            Application.AddMessageFilter(new EdgeResizer(this));
            sensors.Start();
            ThreadPool.QueueUserWorkItem(delegate { SysInfo.LoadHardware(); });
            // Первый запуск: запомнить «как было до Tunex»; потом показать время последнего бэкапа
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Backups.EnsureOriginal(); } catch { }
                try { if (IsHandleCreated) BeginInvoke(new Action(UpdateBackupButton)); } catch { }
            });
            Shown += delegate { UpdateBackupButton(); AfterUpdate(); AutoUpdate(); };
            string start = "home";
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 1; i < args.Length - 1; i++) if (args[i] == "--page" && (factories.ContainsKey(args[i + 1]) || Array.IndexOf(OptimizePage.Sections, args[i + 1]) >= 0)) start = args[i + 1];
            Navigate(start);
            tick.Start();
            // Для проверки интерфейса: --tweak <id>, --wizard, --bench
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i], next = i + 1 < args.Length ? args[i + 1] : null;
                if (a == "--tweak" && next != null) Shown += delegate { ShowTweak(Catalog.Resolve(next), null); };
                if (a == "--wizard") Shown += delegate { StartWizard(); };
                if (a == "--bench") Shown += delegate { RunBench(); };
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x20000;      // WS_MINIMIZEBOX — сворачивание по клику на панели задач
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int v = 2; Native.DwmSetWindowAttribute(Handle, 33, ref v, 4);   // скруглённые углы Windows 11
            int dark = 1; Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4);
        }

        // Изменение размера окна без рамки: ловим мышь у краёв окна, даже если там дочерний элемент
        class EdgeResizer : IMessageFilter
        {
            readonly MainForm f;
            public EdgeResizer(MainForm form) { f = form; }

            int HitCode()
            {
                if (f.WindowState != FormWindowState.Normal || !f.ContainsFocus && Form.ActiveForm != f) return 0;
                Point p = f.PointToClient(Cursor.Position);
                int b = Theme.S(6);
                if (p.X < -1 || p.Y < -1 || p.X > f.Width || p.Y > f.Height) return 0;
                bool l = p.X < b, r = p.X >= f.Width - b, t = p.Y < b, bt = p.Y >= f.Height - b;
                if (t && l) return 13; if (t && r) return 14; if (bt && l) return 16; if (bt && r) return 17;
                if (l) return 10; if (r) return 11; if (t) return 12; if (bt) return 15;
                return 0;
            }

            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != 0x200 && m.Msg != 0x201) return false; // WM_MOUSEMOVE, WM_LBUTTONDOWN
                int ht = HitCode();
                if (ht == 0) return false;
                Cursor c = ht == 10 || ht == 11 ? Cursors.SizeWE : ht == 12 || ht == 15 ? Cursors.SizeNS : ht == 13 || ht == 17 ? Cursors.SizeNWSE : Cursors.SizeNESW;
                Cursor.Current = c;
                if (m.Msg == 0x201)
                {
                    Native.ReleaseCapture();
                    Native.SendMessage(f.Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)ht, IntPtr.Zero);
                    return true;
                }
                return false;
            }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == Native.WM_NCHITTEST && WindowState == FormWindowState.Normal)
            {
                Point p = PointToClient(new Point(m.LParam.ToInt32() & 0xFFFF, (short)(m.LParam.ToInt32() >> 16)));
                int b = Theme.S(6);
                bool l = p.X < b, r = p.X >= Width - b, t = p.Y < b, bt = p.Y >= Height - b;
                if (t && l) m.Result = (IntPtr)13; else if (t && r) m.Result = (IntPtr)14;
                else if (bt && l) m.Result = (IntPtr)16; else if (bt && r) m.Result = (IntPtr)17;
                else if (l) m.Result = (IntPtr)10; else if (r) m.Result = (IntPtr)11;
                else if (t) m.Result = (IntPtr)12; else if (bt) m.Result = (IntPtr)15;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Padding = WindowState == FormWindowState.Maximized ? new Padding(0) : new Padding(1);
            if (title != null) title.Invalidate();
        }

        public void ToggleMaximize()
        {
            if (WindowState == FormWindowState.Maximized) WindowState = FormWindowState.Normal;
            else
            {
                Rectangle wa = Screen.FromHandle(Handle).WorkingArea, sb = Screen.FromHandle(Handle).Bounds;
                MaximizedBounds = new Rectangle(wa.X - sb.X, wa.Y - sb.Y, wa.Width, wa.Height);
                WindowState = FormWindowState.Maximized;
            }
        }

        // Сменить цвет акцента: все страницы пересоздаются, чтобы взять новый цвет
        public void SetAccent(Color c)
        {
            Theme.SetAccent(c);
            App.Manager.State.Accent = Theme.ToHex(c);
            App.Manager.State.Save();
            BeginInvoke(new Action(delegate
            {
                string key = currentKey;
                content.SuspendLayout();
                foreach (Page p in new List<Page>(pages.Values)) { content.Controls.Remove(p); p.Dispose(); }
                pages.Clear(); current = null; currentKey = null;
                Navigate(key ?? "home");
                content.ResumeLayout();
                sidebar.Invalidate(true); title.Invalidate(true); Invalidate(true);
            }));
        }

        public void Navigate(string key)
        {
            // Бывшие отдельные разделы — теперь части страницы «Оптимизация»
            if (Array.IndexOf(OptimizePage.Sections, key) >= 0)
            {
                Navigate("tune");
                OptimizePage op = current as OptimizePage;
                if (op != null) { string k = key; if (IsHandleCreated) BeginInvoke(new Action(delegate { op.JumpTo(k); })); else Shown += delegate { op.JumpTo(k); }; }
                return;
            }
            if (key == currentKey) return;
            Page p;
            if (!pages.TryGetValue(key, out p))
            {
                p = factories[key]();
                p.Dock = DockStyle.Fill;
                pages[key] = p;
                content.Controls.Add(p);
                p.SendToBack();
            }
            if (current != null) current.Visible = false;
            current = p; currentKey = key;
            p.Visible = true;
            p.OnShow();
            p.FitWidths();
            sidebar.SetActive(key);
            toast.BringToFront();
        }

        // ---------------------------------------------------------------- Окно настройки, мастер, проверка
        public void ShowTweak(Tweak t, Control from)
        {
            modal.Open(this, new TweakDialog(t, 0, 0));
        }

        public void CloseModal() { if (modal.IsOpen) modal.CloseModal(); }

        // Первый запуск новой версии после автообновления
        void AfterUpdate()
        {
            AppState s = App.Manager.State;
            if (s.PendingVersion == null) return;
            Version v = Updater.Parse(s.PendingVersion);
            if (v != null && v <= Updater.Current)
            {
                ShowToast("Tunex обновлён до версии " + Theme.Version + ".", Theme.Green, 6000);
                s.PendingVersion = null; s.Save();
            }
        }

        // Тихое автообновление: раз в несколько часов при запуске
        void AutoUpdate()
        {
            AppState s = App.Manager.State;
            if (s.NoAutoUpdate || !Updater.Configured || !Updater.Due(s)) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                ReleaseInfo r; bool installed;
                Updater.CheckAndInstall(s, out r, out installed);
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        s.Save();
                        if (installed) ShowToast("Скачана новая версия Tunex " + r.Version.ToString(2) + " — она откроется при следующем запуске.", Theme.Green, 6000);
                    }));
                }
                catch { }
            });
        }

        public void UpdateBackupButton()
        {
            BackupFile b = null;
            try { b = Backups.Latest(); } catch { }
            title.Backup.SetTime(b);
            title.PerformLayout();
        }

        public void RefreshCurrent() { if (current != null) current.OnShow(); }

        public void RunBench() { modal.Open(this, new BenchDialog()); }

        public void StartWizard()
        {
            List<Tweak> list = new List<Tweak>();
            bool skippedAdmin = false;
            foreach (KeyValuePair<string, Tweak> kv in Catalog.All)
            {
                Tweak t = kv.Value;
                if (t.Rec != Recommend.All || kv.Key == "session") continue;
                TweakState s = TweakState.Of(t);
                if (!s.Available || s.On) continue;
                if (s.AdminBlocked) { skippedAdmin = true; continue; }
                list.Add(t);
            }
            if (list.Count == 0)
            {
                ShowToast(skippedAdmin ? "Остались только настройки, которым нужны права администратора. Перезапустите программу от администратора."
                                       : "Все рекомендованные настройки уже включены.", skippedAdmin ? Theme.Orange : Theme.Green, 6000);
                return;
            }
            WizardStep(list, 0);
        }

        void WizardStep(List<Tweak> list, int i)
        {
            if (i >= list.Count)
            {
                CloseModal();
                ShowToast("Пошаговая настройка завершена. Изменения можно откатить в «Журнале».", Theme.Green, 6000);
                RefreshCurrent();
                return;
            }
            TweakDialog d = new TweakDialog(list[i], i + 1, list.Count);
            d.WizardAnswer = delegate(bool apply)
            {
                if (!apply) { WizardStep(list, i + 1); return; }
                d.Enabled = false;
                ApplyTweak(list[i], true, delegate { WizardStep(list, i + 1); });
            };
            modal.Open(this, d);
        }

        // Применить настройку: подтверждение → (раз в день) бэкап → в фоне → журнал
        public void ApplyTweak(Tweak t, bool on, Action done)
        {
            if (on && t.ConfirmText != null && !DarkDialog.Ask(this, t.Title, t.ConfirmText, "Продолжить", "Отмена")) { done(); return; }
            Action go = delegate
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { Backups.AutoDaily(); } catch { }   // раз в день — бэкап перед первым изменением
                    JournalEntry e;
                    string err = App.Manager.ApplyToggleWork(t, on, out e);
                    BeginInvoke(new Action(delegate
                    {
                        if (err != null) App.Error(t.Title + ": " + err);
                        else
                        {
                            App.Manager.CommitFromUi(e);
                            ShowToast((on ? "Включено: " : "Выключено: ") + t.Title + ". "
                                + (t.NeedsReboot ? "Заработает после перезагрузки." : "Слежу за системой минуту — результат на «Главной»."), Theme.Green, 5000);
                        }
                        if (!modal.IsOpen) RefreshCurrent();
                        done();
                    }));
                });
            };

            go();
        }

        // ---------------------------------------------------------------- Слежение за играми
        string runningGame; int watchTick;

        void WatchGames()
        {
            AppState st = App.Manager.State;
            bool lower = st.Flags.Contains("lowerbg"), mem = st.Flags.Contains("cleanmem");
            if (!lower && !mem && runningGame == null) return;
            string g = BackgroundPriority.RunningGame(st.Games);
            if (g != null && runningGame == null)
            {
                runningGame = g;
                int n = lower ? BackgroundPriority.Lower() : 0;
                string game = g;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    long freed = 0; string err = mem && SysInfo.IsAdmin ? MemoryTools.Clean(out freed) : null;
                    string msg = "Запущена игра " + game + ".";
                    if (lower) msg += " Фоновые программы притормозил: " + n + ".";
                    if (mem && err == null && SysInfo.IsAdmin) msg += " Освободил память: " + (freed >> 20) + " МБ.";
                    BeginInvoke(new Action(delegate { ShowToast(msg, Theme.Accent, 6000); }));
                });
            }
            else if (g != null && lower && ++watchTick % 10 == 0) BackgroundPriority.Lower(); // новые фоновые процессы
            else if (g == null && runningGame != null)
            {
                runningGame = null;
                if (BackgroundPriority.Active) { BackgroundPriority.RestoreAll(); ShowToast("Игра закрыта — фоновые программы снова работают как обычно.", Theme.Accent, 4000); }
            }
        }

        public void ShowToast(string text, Color tint, int ms) { toast.ShowText(text, tint, ms); }

        public void RestartAsAdmin()
        {
            try
            {
                ProcessStartInfo si = new ProcessStartInfo(Application.ExecutablePath);
                si.UseShellExecute = true; si.Verb = "runas";
                Process.Start(si);
                Application.Exit();
            }
            catch (Win32Exception) { } // пользователь нажал «Нет» в UAC
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            tick.Stop();
            BackgroundPriority.RestoreAll();
            sensors.Dispose();
            Displays.ReleaseAll();
            App.Manager.State.Save();
            base.OnFormClosed(e);
        }
    }
}
