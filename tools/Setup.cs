using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

// Установщик Tunex: один файл Tunex-Setup.exe, внутри — Tunex.exe.
// Выбор папки, ярлыки, запись в «Приложения и возможности» Windows, удаление.
// Сборка: build-setup.cmd
namespace TunexSetup
{
    static class Program
    {
        public const string AppName = "Tunex";
        public const string Version = "0.3";   // держать в паре с Theme.Version в src\Core.cs
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Tunex";
        public const string SettingsKey = @"Software\Tunex";

        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                bool quiet = Array.IndexOf(args, "/quiet") >= 0;
                if (args.Length >= 1 && args[0] == "/uninstall") return Uninstaller.Start(quiet);
                if (args.Length >= 2 && args[0] == "/uninstall-run") return Uninstaller.Run(args[1], quiet);
                string dir = null; bool desk = true, start = true, launch = true, auto = false;
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "/dir" && i + 1 < args.Length) dir = args[++i];
                    if (args[i] == "/nodesktop") desk = false;
                    if (args[i] == "/nostartmenu") start = false;
                    if (args[i] == "/nolaunch") launch = false;
                    if (args[i] == "/auto") auto = true;   // после перезапуска от администратора — сразу ставить
                    if (args[i] == "/silent") { auto = true; launch = false; }
                }
                if (auto && dir != null)
                {
                    string err = Installer.Install(dir, desk, start);
                    if (err != null) { if (Array.IndexOf(args, "/silent") < 0) MessageBox.Show(err, "Установка Tunex", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
                    if (Array.IndexOf(args, "/silent") < 0) Installer.Finish(dir, launch);
                    return 0;
                }
                Application.Run(new SetupForm(dir));
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Установка Tunex", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        public static bool IsAdmin
        {
            get { using (WindowsIdentity id = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }
        }

        public static string DefaultDir
        {
            get
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(SettingsKey))
                {
                    string prev = k != null ? k.GetValue("InstallDir") as string : null;
                    if (!string.IsNullOrEmpty(prev)) return prev;
                }
                return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"), AppName);
            }
        }
    }

    // ---------------------------------------------------------------- Установка
    static class Installer
    {
        static string Desktop { get { return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); } }
        static string StartMenu { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Program.AppName + ".lnk"); } }

        public static bool CanWrite(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string t = Path.Combine(dir, ".tunex-write-test");
                File.WriteAllText(t, "1"); File.Delete(t);
                return true;
            }
            catch { return false; }
        }

        // Закрыть запущенный Tunex из этой папки
        public static bool CloseRunning(string exe, bool ask)
        {
            foreach (Process p in Process.GetProcessesByName("Tunex"))
            {
                string path = null;
                try { path = p.MainModule.FileName; } catch { }
                if (path != null && !string.Equals(Path.GetFullPath(path), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase)) continue;
                if (ask && MessageBox.Show("Tunex сейчас открыт. Закрыть его, чтобы продолжить?", "Tunex", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return false;
                try { p.CloseMainWindow(); if (!p.WaitForExit(4000)) { p.Kill(); p.WaitForExit(3000); } } catch { }
                ask = false;
            }
            return true;
        }

        public static string Install(string dir, bool desktop, bool startMenu)
        {
            try
            {
                dir = Path.GetFullPath(dir);
                Directory.CreateDirectory(dir);
                string exe = Path.Combine(dir, "Tunex.exe");
                if (!CloseRunning(exe, true)) return "Установка отменена: Tunex открыт.";

                // Программа
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Tunex.exe"))
                {
                    if (s == null) return "Установщик повреждён: внутри нет Tunex.exe.";
                    string tmp = exe + ".new";
                    using (FileStream f = File.Create(tmp)) s.CopyTo(f);
                    if (File.Exists(exe)) File.Delete(exe);
                    File.Move(tmp, exe);
                }
                // Удаление: копия установщика
                string uninst = Path.Combine(dir, "Uninstall.exe");
                string self = Assembly.GetExecutingAssembly().Location;
                if (!string.Equals(Path.GetFullPath(self), uninst, StringComparison.OrdinalIgnoreCase)) File.Copy(self, uninst, true);

                // Ярлыки
                if (desktop) Shortcut(Path.Combine(Desktop, Program.AppName + ".lnk"), exe, dir);
                else TryDelete(Path.Combine(Desktop, Program.AppName + ".lnk"));
                if (startMenu) Shortcut(StartMenu, exe, dir);

                // «Приложения и возможности»
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Program.UninstallKey))
                {
                    k.SetValue("DisplayName", Program.AppName);
                    k.SetValue("DisplayVersion", Program.Version);
                    k.SetValue("Publisher", Program.AppName);
                    k.SetValue("DisplayIcon", exe);
                    k.SetValue("InstallLocation", dir);
                    k.SetValue("UninstallString", "\"" + uninst + "\" /uninstall");
                    k.SetValue("QuietUninstallString", "\"" + uninst + "\" /uninstall /quiet");
                    k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                    k.SetValue("EstimatedSize", (int)((new FileInfo(exe).Length + new FileInfo(uninst).Length) / 1024), RegistryValueKind.DWord);
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                }
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Program.SettingsKey))
                {
                    k.SetValue("InstallDir", dir);
                    k.SetValue("Desktop", desktop ? 1 : 0, RegistryValueKind.DWord);
                }
                return null;
            }
            catch (UnauthorizedAccessException) { return "Нет доступа к папке " + dir + ". Выберите другую или запустите установку от администратора."; }
            catch (Exception ex) { return "Не удалось установить: " + ex.Message; }
        }

        public static void Finish(string dir, bool launch)
        {
            string exe = Path.Combine(Path.GetFullPath(dir), "Tunex.exe");
            if (launch)
            {
                try { Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = true }); } catch { }
            }
        }

        // Ярлык .lnk через WScript.Shell (есть в любой Windows)
        public static void Shortcut(string lnk, string target, string workDir)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(t);
            try
            {
                object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                Type st = sc.GetType();
                st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
                st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { workDir });
                st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
                st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "Настройка Windows для игр" });
                st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
                Marshal.ReleaseComObject(sc);
            }
            finally { Marshal.ReleaseComObject(shell); }
        }

        public static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }

        public static void RemoveShortcuts()
        {
            TryDelete(Path.Combine(Desktop, Program.AppName + ".lnk"));
            TryDelete(StartMenu);
        }
    }

    // ---------------------------------------------------------------- Удаление
    static class Uninstaller
    {
        // Запуск из папки программы: копируем себя во временную папку, чтобы папку можно было удалить целиком
        public static int Start(bool quiet)
        {
            string self = Assembly.GetExecutingAssembly().Location;
            string dir = Path.GetDirectoryName(self);
            string tmp = Path.Combine(Path.GetTempPath(), "Tunex-Uninstall.exe");
            File.Copy(self, tmp, true);
            Process.Start(new ProcessStartInfo(tmp, "/uninstall-run \"" + dir + "\"" + (quiet ? " /quiet" : "")) { UseShellExecute = true });
            return 0;
        }

        // quiet — без вопросов: Tunex закрывается, журнал и настройки остаются
        public static int Run(string dir, bool quiet)
        {
            if (!quiet && MessageBox.Show("Удалить Tunex из папки\n" + dir + "?\n\n" +
                "Настройки Windows, которые вы меняли в Tunex, останутся как есть. Чтобы вернуть всё как было, " +
                "перед удалением откройте в Tunex «Журнал» → «Вернуть всё».",
                "Удаление Tunex", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return 1;

            string exe = Path.Combine(dir, "Tunex.exe");
            if (!Installer.CloseRunning(exe, !quiet)) return 1;

            bool wipeData = !quiet && MessageBox.Show("Удалить также журнал изменений и настройки Tunex?\n(" +
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tunex") + ")\n\n" +
                "Если оставить — при повторной установке журнал и «Вернуть всё» продолжат работать.",
                "Удаление Tunex", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

            Installer.RemoveShortcuts();
            Installer.TryDelete(exe);
            Installer.TryDelete(Path.Combine(dir, "Uninstall.exe"));
            try { if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(Program.UninstallKey, false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(Program.SettingsKey, false); } catch { }
            if (wipeData) { try { Directory.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tunex"), true); } catch { } }

            if (!quiet) MessageBox.Show(File.Exists(exe) ? "Tunex удалён не полностью: файл " + exe + " занят. Удалите его вручную." : "Tunex удалён.",
                "Удаление Tunex", MessageBoxButtons.OK, MessageBoxIcon.Information);

            // Удалить временную копию удалятора после выхода
            string self = Assembly.GetExecutingAssembly().Location;
            try { Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & del /f /q \"" + self + "\"") { CreateNoWindow = true, UseShellExecute = false }); } catch { }
            return 0;
        }
    }

    // ---------------------------------------------------------------- Окно установки
    class SetupForm : Form
    {
        static readonly Color Bg = Color.FromArgb(19, 19, 18), Card = Color.FromArgb(36, 36, 33), Border = Color.FromArgb(70, 69, 63);
        static readonly Color Text1 = Color.FromArgb(234, 233, 228), Text2 = Color.FromArgb(158, 156, 148), Accent = Color.FromArgb(198, 240, 92);
        readonly TextBox path;
        readonly CheckBox desk, start, launch;
        readonly Button install, cancel, browse;
        readonly Label status;

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);

        public SetupForm(string dir)
        {
            Text = "Установка Tunex " + Program.Version;
            Font = new Font("Segoe UI", 9.75f);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg; ForeColor = Text1;
            ClientSize = new Size(560, 338);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Label title = new Label(); title.Text = "Tunex"; title.Font = new Font(HasFont("Bahnschrift SemiBold") ? "Bahnschrift SemiBold" : "Segoe UI Semibold", 20f);
            title.AutoSize = true; title.Location = new Point(28, 22);
            Label sub = new Label(); sub.Text = "Настройка Windows для игр · версия " + Program.Version; sub.ForeColor = Text2; sub.AutoSize = true; sub.Location = new Point(31, 64);

            Label pl = new Label(); pl.Text = "Папка установки"; pl.AutoSize = true; pl.Location = new Point(30, 106);
            path = new TextBox(); path.Text = dir ?? Program.DefaultDir; path.BackColor = Card; path.ForeColor = Text1; path.BorderStyle = BorderStyle.FixedSingle;
            path.Location = new Point(32, 130); path.Width = 390;
            browse = MakeButton("Обзор…", false); browse.Location = new Point(432, 128); browse.Size = new Size(96, 28);
            browse.Click += delegate { Browse(); };

            desk = MakeCheck("Ярлык на рабочем столе", 172, true);
            start = MakeCheck("Ярлык в меню «Пуск»", 198, true);
            launch = MakeCheck("Запустить Tunex после установки", 224, true);

            status = new Label(); status.ForeColor = Text2; status.AutoSize = false; status.Location = new Point(30, 286); status.Size = new Size(280, 44);
            status.Text = "Права администратора не нужны.";

            install = MakeButton("Установить", true); install.Location = new Point(318, 284); install.Size = new Size(120, 34);
            cancel = MakeButton("Отмена", false); cancel.Location = new Point(448, 284); cancel.Size = new Size(80, 34);
            install.Click += delegate { DoInstall(); };
            cancel.Click += delegate { Close(); };
            AcceptButton = install; CancelButton = cancel;
            Shown += delegate { ActiveControl = install; path.SelectionStart = 0; path.SelectionLength = 0; };

            Controls.AddRange(new Control[] { title, sub, pl, path, browse, desk, start, launch, status, install, cancel });
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = 1; try { DwmSetWindowAttribute(Handle, 20, ref dark, 4); } catch { }
        }

        static bool HasFont(string name) { using (Font f = new Font(name, 10f)) return f.Name == name; }

        CheckBox MakeCheck(string text, int y, bool on)
        {
            CheckBox c = new CheckBox(); c.Text = text; c.Checked = on; c.AutoSize = true; c.Location = new Point(32, y); c.ForeColor = Text1; c.BackColor = Bg; c.FlatStyle = FlatStyle.Flat; c.FlatAppearance.BorderColor = Border; c.FlatAppearance.CheckedBackColor = Card;
            return c;
        }

        Button MakeButton(string text, bool primary)
        {
            Button b = new Button(); b.Text = text; b.FlatStyle = FlatStyle.Flat; b.Cursor = Cursors.Hand;
            b.BackColor = primary ? Accent : Card; b.ForeColor = primary ? Color.FromArgb(18, 22, 6) : Text1;
            b.FlatAppearance.BorderColor = primary ? Accent : Border; b.FlatAppearance.BorderSize = 1;
            if (primary) b.Font = new Font("Segoe UI Semibold", 9.75f);
            return b;
        }

        void Browse()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "Где установить Tunex? Внутри выбранной папки будет создана папка Tunex.";
                d.ShowNewFolderButton = true;
                try { string p = Path.GetDirectoryName(path.Text); if (Directory.Exists(p)) d.SelectedPath = p; } catch { }
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string sel = d.SelectedPath;
                if (!sel.TrimEnd('\\').EndsWith("\\" + Program.AppName, StringComparison.OrdinalIgnoreCase)) sel = Path.Combine(sel, Program.AppName);
                path.Text = sel;
            }
        }

        void DoInstall()
        {
            string dir;
            try { dir = Path.GetFullPath(path.Text.Trim()); }
            catch { MessageBox.Show(this, "Неверный путь к папке.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            // Папка требует прав администратора (например, Program Files) — перезапуск установщика с запросом прав
            if (!Installer.CanWrite(dir))
            {
                if (Program.IsAdmin) { MessageBox.Show(this, "Нет доступа к папке " + dir, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                if (MessageBox.Show(this, "Для установки в эту папку нужны права администратора. Продолжить?", Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                string a = "/auto /dir \"" + dir + "\"" + (desk.Checked ? "" : " /nodesktop") + (start.Checked ? "" : " /nostartmenu") + (launch.Checked ? "" : " /nolaunch");
                try { Process.Start(new ProcessStartInfo(Application.ExecutablePath, a) { Verb = "runas", UseShellExecute = true }); Close(); }
                catch { /* пользователь отказал в правах */ }
                return;
            }

            install.Enabled = cancel.Enabled = browse.Enabled = false;
            status.Text = "Устанавливаю…"; status.ForeColor = Text1; Refresh();
            string err = Installer.Install(dir, desk.Checked, start.Checked);
            if (err != null)
            {
                status.Text = err; status.ForeColor = Color.FromArgb(240, 96, 78);
                install.Enabled = cancel.Enabled = browse.Enabled = true;
                return;
            }
            Installer.Finish(dir, launch.Checked);
            MessageBox.Show(this, "Tunex установлен в\n" + dir + (launch.Checked ? "" : "\n\nЗапустить можно ярлыком на рабочем столе или в меню «Пуск»."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
    }
}
