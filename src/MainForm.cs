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

        void Fit() { Size = new Size(Theme.S(42) + Theme.Measure(label, Theme.FontSmallBold).Width, Theme.S(28)); }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            // gw-pill: маленькая круглая кнопка с серой заливкой
            Theme.FillRound(g, hover ? Theme.CardHover : Theme.CardInner, r, r.Height / 2);
            Theme.Glyph(g, Icons.Save, Theme.FontIconSmall, Theme.Accent, new Rectangle(Theme.S(10), 0, Theme.S(18), Height));
            Theme.DrawText(g, label, Theme.FontSmallBold, Theme.Text, new Rectangle(Theme.S(32), 0, Width - Theme.S(38), Height), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
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

        protected override void OnPaintBackground(PaintEventArgs e) { Theme.PaintBackdrop(e.Graphics, this, ClientRectangle); }

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
                if (i == hoverBtn) Theme.FillRound(g, i == 2 ? Theme.Red : Theme.CardInner, new RectangleF(r.X, r.Y, r.Width, r.Height), r.Height / 2f);
                Theme.Glyph(g, glyphs[i], Theme.FontIconTiny, i == hoverBtn ? (i == 2 ? Color.White : Theme.Text) : Theme.Text2, r);
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
            // gw-nav: выбранный раздел — мягкий фон цвета акцента и значок тем же цветом
            if (Active) Theme.FillRound(g, Theme.AccentSoft, r, Theme.Sf(10));
            else if (hover) Theme.FillRound(g, Theme.CardInner, r, Theme.Sf(10));
            Color c = Active ? Theme.Accent : hover ? Theme.Text : Theme.Text2;
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
                    using (SolidBrush b = new SolidBrush(Theme.Pop)) e.Graphics.FillRectangle(b, e.Bounds);
                    using (Pen p = new Pen(Theme.Frame)) e.Graphics.DrawRectangle(p, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
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
            Theme.PaintBackdrop(g, this, ClientRectangle);
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
            Theme.FillRound(g, Theme.Pop, r, Theme.Sf(14));
            Theme.DrawRound(g, Theme.Alpha(Theme.Accent, 110), r, Theme.Sf(14), 1f);
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
            Theme.Accent = Theme.TryHex(App.Manager.State.Accent, out accent) ? accent : Color.Empty;
            Theme.SetMode(App.Manager.State.ThemeMode);

            Text = Theme.AppName;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Theme.Frame;
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
            factories["fps"] = delegate { return new FpsPage(); };
            factories["prep"] = delegate { return new PlayPage(); };
            factories["lag"] = delegate { return new LagPage(); };
            factories["care"] = delegate { return new CarePage(); };

            sidebar.AddItem("home", Icons.Home, "Главная");
            sidebar.AddItem("tune", Icons.Speed, "Оптимизация");
            sidebar.AddItem("pc", Icons.Cpu, "Оценка ПК");
            sidebar.AddItem("games", Icons.Library, "Игры");
            sidebar.AddItem("prep", Icons.Play, "Перед игрой");
            sidebar.AddItem("fps", Icons.Fps, "Замер FPS");
            sidebar.AddItem("lag", Icons.Lag, "Разбор лагов");
            sidebar.AddItem("care", Icons.Repair, "Обслуживание");
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
                try { FpsMeter.Tick(); } catch { }
                if (current != null) current.OnTick();
                if (++gameTick % 3 == 0) { try { WatchGames(); } catch { } }
                if (gameTick % 15 == 0) UpdateBackupButton();
            };

            FpsMeter.Finished += delegate(FpsRun r, string err)
            {
                if (err != null) { ShowToast("Замер FPS: " + err, Theme.Red, 8000); return; }
                ShowToast("Замер готов: " + r.Game + " — в среднем " + FpsPage.Fps(r.Avg) + " FPS, 1% низкие " + FpsPage.Fps(r.Low1) + ".", Theme.Green, 8000);
                if (currentKey != "fps") Navigate("fps");
            };

            Application.AddMessageFilter(new EdgeResizer(this));
            sensors.Start();
            LagRecorder.Start();
            ThreadPool.QueueUserWorkItem(delegate { SysInfo.LoadHardware(); });
            ThreadPool.QueueUserWorkItem(delegate { try { PresentMon.CleanupSessions(); } catch { } });   // записи FPS после аварийного выхода
            try { QuietMode.End(); } catch { }   // «Не беспокоить» остался включён после аварийного выхода
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
                if (a == "--bottom") Shown += delegate
                {
                    // Каждые 4 с в течение 32 с — страницы вроде «Оценки ПК» и пинга заполняются в фоне
                    System.Windows.Forms.Timer t = new System.Windows.Forms.Timer(); t.Interval = 4000;
                    int ticks = 0;
                    t.Tick += delegate { if (++ticks >= 8) t.Dispose(); if (current != null) { current.AutoScrollPosition = new Point(0, current.DisplayRectangle.Height); current.Invalidate(true); } };
                    t.Start();
                };
                if (a == "--fpssummary") Shown += delegate
                {
                    List<FpsRun> l = App.Manager.State.FpsRuns;
                    if (l.Count > 0) ShowCard(new FpsSummaryDialog(l[l.Count - 1]));
                };
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
            int dark = Theme.Dark ? 1 : 0; Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            RegisterHotKey(Handle, HotkeyFps, 0x0002 | 0x0004 | 0x4000, 0x78);   // Ctrl + Shift + F9, без автоповтора
            LagHotkeyOk = RegisterHotKey(Handle, HotkeyLag, 0x0002 | 0x0004 | 0x4000, 0x79);   // Ctrl + Shift + F10 — разбор лага
        }

        const int HotkeyFps = 0x7F01, HotkeyLag = 0x7F02;
        public static bool LagHotkeyOk = true;
        DateTime lagLast = DateTime.MinValue;

        // Игра, о которой речь: из списка «Игры» или окно на переднем плане
        public string CurrentGame()
        {
            if (runningGame != null) return runningGame;
            try
            {
                int pid = FpsMeter.ForegroundPid();
                if (pid > 0 && pid != System.Diagnostics.Process.GetCurrentProcess().Id)
                    using (System.Diagnostics.Process p = System.Diagnostics.Process.GetProcessById(pid))
                    {
                        string n = p.ProcessName;
                        if (!n.Equals("explorer", StringComparison.OrdinalIgnoreCase) && !FpsCapture.IsNotGame(n + ".exe")) return n;
                    }
            }
            catch { }
            return null;
        }

        void CaptureLag()
        {
            if ((DateTime.Now - lagLast).TotalSeconds < 3) return;   // двойное нажатие — один разбор
            lagLast = DateTime.Now;
            string game = CurrentGame();
            System.Threading.ThreadPool.QueueUserWorkItem(delegate { try { Console.Beep(660, 90); System.Threading.Thread.Sleep(40); Console.Beep(990, 90); } catch { } });
            LagHistory.Capture(game, delegate(LagMoment m)
            {
                if (m == null) { ShowToast("Tunex только что запустился и ещё ничего не записал. Подождите несколько секунд.", Theme.Orange, 6000); return; }
                LagCause c = m.Main;
                ShowToast("Лаг разобран: " + (c != null ? c.Title : "") + ". Подробности — в «Разборе лагов».", c != null ? LagPage.Level(c) : Theme.Accent, 7000);
            });
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);

        public void ShowCard(Control c) { modal.Open(this, c); }

        // Смена темы «Стекло»: светлая, тёмная или как в Windows — перекрашиваем окно и пересобираем разделы
        public void SetThemeMode(string mode)
        {
            Theme.SetMode(mode);
            App.Manager.State.ThemeMode = mode;
            App.Manager.State.Accent = Theme.ToHex(Theme.Accent);
            App.Manager.State.Save();
            if (title.AdminPill != null) title.AdminPill.Tint = SysInfo.IsAdmin ? Theme.Green : Theme.Orange;
            int dark = Theme.Dark ? 1 : 0; Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            BackColor = Theme.Frame; content.BackColor = Theme.Bg; title.BackColor = Theme.Sidebar; sidebar.BackColor = Theme.Sidebar; modal.BackColor = Theme.Bg;
            RebuildPages();
        }

        void RebuildPages()
        {
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
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == HotkeyLag) { CaptureLag(); return; }   // Ctrl + Shift + F10
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == HotkeyFps)   // WM_HOTKEY: Ctrl + Shift + F9 — замер FPS прямо из игры
            {
                string err = FpsMeter.Hotkey();
                if (err != null) ShowToast("Замер FPS не начался: " + err, Theme.Red, 8000);
                if (current != null) current.OnTick();
                return;
            }
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
            RebuildPages();
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
        string runningGame; int watchTick; DateTime gameStarted;
        FpsSession gameFps;   // запись FPS за всю игру для «Итога после игры»

        void RebuildCurrentFps()
        {
            Page p;
            if (pages.TryGetValue("fps", out p)) { content.Controls.Remove(p); pages.Remove("fps"); p.Dispose(); }
            current = null; currentKey = null;
            Navigate("fps");
        }

        void WatchGames()
        {
            AppState st = App.Manager.State;
            bool lower = st.Flags.Contains("lowerbg"), mem = st.Flags.Contains("cleanmem");
            bool unload = st.Flags.Contains("unload"), quiet = st.Flags.Contains("quiet");
            if (!lower && !mem && !unload && !quiet && !st.GameSummary && st.Unloaded.Count == 0 && runningGame == null) return;
            string g = BackgroundPriority.RunningGame(st.Games);
            if (gameFps != null && g != null) gameFps.Sample();
            if (g != null && runningGame == null)
            {
                runningGame = g; gameStarted = DateTime.Now;
                if (st.GameSummary && gameFps == null) gameFps = FpsSession.TryStart(g + ".exe");
                if (!lower && !mem && !unload && !quiet) return;
                int n = lower ? BackgroundPriority.Lower() : 0;
                List<string> calm = quiet ? QuietMode.Begin() : new List<string>();
                string game = g;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    // Сначала закрыть лишние программы, потом чистить память — так освободится больше
                    List<ClosedApp> closed = new List<ClosedApp>(); long unloadedMem = 0;
                    if (unload)
                    {
                        try
                        {
                            List<AppGroup> pick = new List<AppGroup>();
                            foreach (AppGroup a in Unload.Scan()) if (Unload.Picked(a)) pick.Add(a);
                            List<string> failed = new List<string>();
                            if (pick.Count > 0) closed = Unload.Close(pick, failed);
                            foreach (AppGroup a in pick) if (!failed.Contains(a.Name)) unloadedMem += a.Memory;
                        }
                        catch { }
                    }
                    long freed = 0; string err = mem && SysInfo.IsAdmin ? MemoryTools.Clean(out freed) : null;
                    string msg = "Запущена игра " + game + ".";
                    if (closed.Count > 0) msg += " Закрыл до конца игры: " + Names(closed) + " (" + Unload.Size(unloadedMem) + ").";
                    if (lower) msg += " Фоновые программы притормозил: " + n + ".";
                    if (mem && err == null && SysInfo.IsAdmin) msg += " Освободил память: " + (freed >> 20) + " МБ.";
                    if (calm.Count > 0) msg += " Не беспокоить: " + string.Join(" и ", calm.ToArray()) + " подождут.";
                    BeginInvoke(new Action(delegate
                    {
                        st.Unloaded.AddRange(closed); st.Save();
                        ShowToast(msg, Theme.Accent, 7000);
                    }));
                });
            }
            else if (g != null && lower && ++watchTick % 10 == 0) BackgroundPriority.Lower(); // новые фоновые процессы
            else if (g == null && runningGame != null)
            {
                runningGame = null;
                if (gameFps != null)
                {
                    FpsSession s = gameFps; gameFps = null;
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        FpsRun r = null;
                        try { r = s.Finish(); } catch { }
                        if (r == null) return;
                        try
                        {
                            BeginInvoke(new Action(delegate
                            {
                                FpsHistory.Add(r);
                                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                                Activate();
                                if (!modal.IsOpen) ShowCard(new FpsSummaryDialog(r));
                                if (currentKey == "fps") RebuildCurrentFps();
                            }));
                        }
                        catch { }
                    });
                }
                List<string> back = new List<string>();
                int lags = LagHistory.Since(gameStarted);
                if (lags > 0) back.Add("отмечено лагов: " + lags + ", разбор — в «Разборе лагов»");
                if (BackgroundPriority.Active) { BackgroundPriority.RestoreAll(); back.Add("фоновые программы работают как обычно"); }
                if (QuietMode.Active) { QuietMode.End(); back.Add("уведомления и обновления снова включены"); }
                if (st.Unloaded.Count > 0)
                {
                    List<ClosedApp> list = new List<ClosedApp>(st.Unloaded);
                    st.Unloaded.Clear(); st.Save();
                    back.Add("открываю " + Names(list));
                    ThreadPool.QueueUserWorkItem(delegate { try { Unload.Restore(list); } catch { } });
                }
                if (back.Count > 0) ShowToast("Игра закрыта: " + string.Join("; ", back.ToArray()) + ".", Theme.Accent, 6000);
            }
        }

        static string Names(List<ClosedApp> l)
        {
            List<string> n = new List<string>();
            foreach (ClosedApp c in l) if (!n.Contains(c.Name)) n.Add(c.Name);
            return string.Join(", ", n.ToArray());
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
            try { UnregisterHotKey(Handle, HotkeyFps); UnregisterHotKey(Handle, HotkeyLag); } catch { }
            LagRecorder.Stop();
            FpsMeter.Shutdown();
            if (gameFps != null) { try { gameFps.Finish(); } catch { } gameFps = null; }
            BackgroundPriority.RestoreAll();
            try { QuietMode.End(); } catch { }
            sensors.Dispose();
            Displays.ReleaseAll();
            App.Manager.State.Save();
            base.OnFormClosed(e);
        }
    }
}
