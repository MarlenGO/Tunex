using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Tunex
{
    // ---------------------------------------------------------------- Тема
    static class Theme
    {
        public const string AppName = "Tunex";
        public const string Version = "0.1";

        public static float Scale = 1f;

        // Дизайн v2 «Приборная панель»: тёплый графит, один сигнальный лаймовый акцент, острые углы,
        // технический шрифт Bahnschrift для заголовков и цифр. Прежний дизайн — design-backup\v1-teal
        public static readonly Color Bg = Color.FromArgb(19, 19, 18);
        public static readonly Color Sidebar = Color.FromArgb(14, 14, 13);
        public static readonly Color SidebarGlow = Color.FromArgb(14, 14, 13);
        public static readonly Color Card = Color.FromArgb(26, 26, 24);
        public static readonly Color CardInner = Color.FromArgb(36, 36, 33);
        public static readonly Color CardHover = Color.FromArgb(44, 44, 40);
        public static readonly Color Border = Color.FromArgb(50, 49, 45);
        // Акцент меняется в «Оформлении»; оттенки (фон, текст, текст на кнопке) считаются из него
        public static Color Accent = Color.FromArgb(198, 240, 92);      // сигнальный лайм
        public static Color AccentSoft = Color.FromArgb(38, 44, 24);
        public static Color AccentText = Color.FromArgb(212, 245, 138);
        public static Color OnAccent = Color.FromArgb(18, 22, 6);       // текст на кнопке с акцентом

        public static readonly string[] PresetNames = { "Синий", "Бирюзовый", "Лайм" };
        public static readonly Color[] Presets = { Color.FromArgb(92, 152, 255), Color.FromArgb(77, 201, 176), Color.FromArgb(198, 240, 92) };
        public static Color DefaultAccent { get { return Presets[2]; } }

        public static Color OnAccentFor(Color a) { return Blend(a, Color.Black, 0.88f); }
        public static float Luma(Color c) { return (0.299f * c.R + 0.587f * c.G + 0.114f * c.B) / 255f; }

        public static void SetAccent(Color a)
        {
            a = Color.FromArgb(255, a.R, a.G, a.B);
            // Слишком тёмный цвет на тёмном фоне не видно — осветляем
            for (int i = 0; i < 8 && Luma(a) < 0.42f; i++) a = Blend(a, Color.White, 0.15f);
            Accent = a;
            AccentSoft = Blend(Bg, a, 0.16f);
            AccentText = Blend(a, Color.White, 0.25f);
            OnAccent = Luma(a) > 0.55f ? Blend(a, Color.Black, 0.88f) : Color.White;
            Logo.Accent = a; Logo.AccentDeep = Blend(a, Color.Black, 0.4f);
        }

        public static string ToHex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }
        public static bool TryHex(string s, out Color c)
        {
            c = Color.Empty;
            if (string.IsNullOrEmpty(s) || s.Length != 7 || s[0] != '#') return false;
            int v;
            if (!int.TryParse(s.Substring(1), System.Globalization.NumberStyles.HexNumber, null, out v)) return false;
            c = Color.FromArgb((v >> 16) & 255, (v >> 8) & 255, v & 255);
            return true;
        }
        public static readonly Color Green = Color.FromArgb(178, 228, 96);       // «применено» — родственник акцента
        public static readonly Color Blue = Color.FromArgb(122, 172, 230);
        public static readonly Color Orange = Color.FromArgb(240, 168, 64);
        public static readonly Color Red = Color.FromArgb(240, 96, 78);
        public static readonly Color Purple = Color.FromArgb(176, 150, 236);
        public static readonly Color Text = Color.FromArgb(234, 233, 228);
        public static readonly Color Text2 = Color.FromArgb(158, 156, 148);
        public static readonly Color Text3 = Color.FromArgb(110, 108, 101);
        public static readonly Color ToggleOff = Color.FromArgb(62, 61, 56);
        public static readonly Color Button = Color.FromArgb(34, 34, 31);
        public static readonly Color ButtonBorder = Color.FromArgb(70, 69, 63);

        // Все скругления не больше 3 px — острые «приборные» углы
        public static float MaxRadius { get { return Sf(3); } }

        // Разрядка для заглавных подписей: тонкий пробел между буквами
        public static string Track(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.Length > 16) return s.ToUpperInvariant();
            System.Text.StringBuilder b = new System.Text.StringBuilder(s.Length * 2);
            foreach (char c in s.ToUpperInvariant()) { if (b.Length > 0) b.Append('\u200A'); b.Append(c); }
            return b.ToString();
        }

        public static Font FontBody, FontBodyBold, FontSmall, FontSmallBold, FontNav, FontTitle, FontH2, FontBig, FontIcon, FontIconBig, FontIconSmall, FontIconTiny, FontIconNav, FontLogo, FontLabel, FontNum, FontCellTitle;

        public static void Init()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96f;
            FontBody = new Font("Segoe UI", 9.75f);
            FontBodyBold = new Font("Segoe UI Semibold", 9.75f);
            FontCellTitle = new Font("Segoe UI Semibold", 11.25f);
            FontSmall = new Font("Segoe UI", 8.5f);
            FontNav = new Font("Segoe UI Semibold", 9.75f);
            // Заголовки, подписи и цифры — Bahnschrift (есть в Windows 10 1709+), иначе Segoe UI
            FontSmallBold = Display("Bahnschrift SemiBold", 8.5f, "Segoe UI Semibold", 8f);
            FontTitle = Display("Bahnschrift SemiBold", 19f, "Segoe UI Semibold", 17f);
            FontH2 = Display("Bahnschrift SemiBold", 11f, "Segoe UI Semibold", 10.5f);
            FontBig = Display("Bahnschrift SemiBold", 20f, "Segoe UI Semibold", 16f);
            FontLogo = Display("Bahnschrift SemiBold", 11f, "Segoe UI Semibold", 11f);
            FontLabel = Display("Bahnschrift SemiBold", 8f, "Segoe UI Semibold", 7.5f);
            FontNum = Display("Bahnschrift", 10f, "Segoe UI", 9.75f);
            string icons = HasFont("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
            FontIcon = new Font(icons, 11f);
            FontIconBig = new Font(icons, 15f);
            FontIconSmall = new Font(icons, 9f);
            FontIconTiny = new Font(icons, 7f);
            FontIconNav = new Font(icons, 13f);
        }

        static Font Display(string name, float size, string fallback, float fallbackSize)
        {
            return HasFont(name) ? new Font(name, size) : new Font(fallback, fallbackSize);
        }

        static bool HasFont(string name)
        {
            using (Font f = new Font(name, 10f)) return f.Name == name;
        }

        public static int S(int v) { return (int)Math.Round(v * Scale); }
        public static float Sf(float v) { return v * Scale; }

        public static Color Blend(Color a, Color b, float t)
        {
            if (t < 0) t = 0; if (t > 1) t = 1;
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static Color Alpha(Color c, int a) { return Color.FromArgb(a, c.R, c.G, c.B); }

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        public static GraphicsPath RoundPath(RectangleF r, float rad, bool tl, bool tr, bool br, bool bl)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(Math.Min(rad, MaxRadius) * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            if (tl) p.AddArc(r.X, r.Y, d, d, 180, 90); else p.AddLine(r.X, r.Y, r.X, r.Y);
            if (tr) p.AddArc(r.Right - d, r.Y, d, d, 270, 90); else p.AddLine(r.Right, r.Y, r.Right, r.Y);
            if (br) p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); else p.AddLine(r.Right, r.Bottom, r.Right, r.Bottom);
            if (bl) p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); else p.AddLine(r.X, r.Bottom, r.X, r.Bottom);
            p.CloseFigure();
            return p;
        }

        public static GraphicsPath RoundPath(RectangleF r, float rad) { return RoundPath(r, rad, true, true, true, true); }

        public static void FillRound(Graphics g, Color c, RectangleF r, float rad)
        {
            using (GraphicsPath p = RoundPath(r, rad)) using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void DrawRound(Graphics g, Color c, RectangleF r, float rad, float w)
        {
            using (GraphicsPath p = RoundPath(r, rad)) using (Pen pen = new Pen(c, w)) g.DrawPath(pen, p);
        }

        public static void DrawText(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, s, f, r, c, flags | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        public static void Glyph(Graphics g, string glyph, Font f, Color c, Rectangle r)
        {
            TextRenderer.DrawText(g, glyph, f, r, c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        public static Size Measure(string s, Font f, int width)
        {
            if (string.IsNullOrEmpty(s)) return Size.Empty;
            return TextRenderer.MeasureText(s, f, new Size(Math.Max(1, width), int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
        }

        public static Size Measure(string s, Font f)
        {
            if (string.IsNullOrEmpty(s)) return Size.Empty;
            return TextRenderer.MeasureText(s, f, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }

    // Иконки шрифта Segoe MDL2 Assets / Segoe Fluent Icons
    static class Icons
    {
        public const string Home = "";
        public const string Power = "";
        public const string Game = "";
        public const string System = "";
        public const string Library = "";
        public const string Display = "";
        public const string Chip = "";
        public const string History = "";
        public const string Cpu = "";
        public const string Memory = "";
        public const string Gpu = "";
        public const string Thermo = "";
        public const string Shield = "";
        public const string Warning = "";
        public const string Info = "";
        public const string Check = "";
        public const string Add = "";
        public const string Close = "";
        public const string Min = "";
        public const string Max = "";
        public const string Restore = "";
        public const string Chevron = "";
        public const string Undo = "";
        public const string Refresh = "";
        public const string Delete = "";
        public const string Eye = "";
        public const string Brightness = "";
        public const string Play = "";
        public const string Speed = "\uEC4A";
        public const string Settings = "\uE713";
        public const string Palette = "\uE790";
        public const string Search = "\uE721";
        public const string Save = "\uE74E";
        public const string Network = "";
        public const string Broom = "";
    }

    // ---------------------------------------------------------------- WinAPI
    static class Native
    {
        public const int WM_NCLBUTTONDOWN = 0xA1, WM_NCHITTEST = 0x84, HTCAPTION = 2;

        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr h, string app, string idList);
        [DllImport("kernel32.dll")] public static extern IntPtr LocalFree(IntPtr p);

        // мышь
        [DllImport("user32.dll", SetLastError = true)] public static extern bool SystemParametersInfo(int action, int param, int[] vparam, int winIni);

        // память
        [StructLayout(LayoutKind.Sequential)]
        public class MEMORYSTATUSEX
        {
            public uint dwLength = 64; public uint dwMemoryLoad; public ulong ullTotalPhys; public ulong ullAvailPhys;
            public ulong ullTotalPageFile; public ulong ullAvailPageFile; public ulong ullTotalVirtual; public ulong ullAvailVirtual; public ulong ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX m);

        // электропитание
        [DllImport("powrprof.dll")] public static extern uint PowerEnumerate(IntPtr root, IntPtr scheme, IntPtr sub, uint flags, uint index, byte[] buf, ref uint size);
        [DllImport("powrprof.dll")] public static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr sub, IntPtr setting, byte[] buf, ref uint size);
        [DllImport("powrprof.dll")] public static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
        [DllImport("powrprof.dll")] public static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
        [DllImport("powrprof.dll")] public static extern uint PowerDuplicateScheme(IntPtr root, ref Guid src, ref IntPtr dest);
        [DllImport("powrprof.dll")] public static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
        [DllImport("powrprof.dll")] public static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
        [DllImport("powrprof.dll")] public static extern uint PowerWriteFriendlyName(IntPtr root, ref Guid scheme, IntPtr sub, IntPtr setting, byte[] buf, uint size);
        // «Режим питания» из Параметров Windows (ползунок) — Windows 10 1709+
        [DllImport("powrprof.dll")] public static extern uint PowerGetEffectiveOverlayScheme(out Guid overlay);
        [DllImport("powrprof.dll")] public static extern uint PowerSetActiveOverlayScheme(ref Guid overlay);
        [DllImport("powrprof.dll")] public static extern uint CallNtPowerInformation(int level, IntPtr inBuf, uint inSize, byte[] outBuf, uint outSize);

        // мониторы
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        public delegate bool MonitorEnumProc(IntPtr hMon, IntPtr hdc, ref RECT r, IntPtr data);
        [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFOEX
        {
            public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFOEX mi);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PHYSICAL_MONITOR
        {
            public IntPtr hPhysicalMonitor;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription;
        }
        [DllImport("dxva2.dll")] public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMon, out uint count);
        [DllImport("dxva2.dll")] public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMon, uint count, [Out] PHYSICAL_MONITOR[] arr);
        [DllImport("dxva2.dll")] public static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] arr);
        [DllImport("dxva2.dll")] public static extern bool GetMonitorBrightness(IntPtr h, out uint min, out uint cur, out uint max);
        [DllImport("dxva2.dll")] public static extern bool SetMonitorBrightness(IntPtr h, uint v);
        [DllImport("dxva2.dll")] public static extern bool GetMonitorContrast(IntPtr h, out uint min, out uint cur, out uint max);
        [DllImport("dxva2.dll")] public static extern bool SetMonitorContrast(IntPtr h, uint v);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr CreateDC(string driver, string device, string output, IntPtr init);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool SetDeviceGammaRamp(IntPtr hdc, ushort[] ramp);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }
        public const int ENUM_CURRENT_SETTINGS = -1, DM_DISPLAYFREQUENCY = 0x400000, CDS_UPDATEREGISTRY = 1;
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplaySettings(string dev, int mode, ref DEVMODE dm);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int ChangeDisplaySettingsEx(string dev, ref DEVMODE dm, IntPtr hwnd, int flags, IntPtr param);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplayDevices(string dev, uint idx, ref DISPLAY_DEVICE dd, uint flags);
    }

    // ---------------------------------------------------------------- Реестр
    static class Reg
    {
        public static RegistryKey Base(string root)
        {
            return RegistryKey.OpenBaseKey(root == "HKLM" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64);
        }

        public static object Get(string root, string path, string name)
        {
            using (RegistryKey b = Base(root))
            using (RegistryKey k = b.OpenSubKey(path))
                return k == null ? null : k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }

        public static RegistryValueKind Kind(string root, string path, string name)
        {
            using (RegistryKey b = Base(root))
            using (RegistryKey k = b.OpenSubKey(path))
            {
                if (k == null) return RegistryValueKind.Unknown;
                try { return k.GetValueKind(name); } catch { return RegistryValueKind.Unknown; }
            }
        }

        public static void Set(string root, string path, string name, object value, RegistryValueKind kind)
        {
            using (RegistryKey b = Base(root))
            using (RegistryKey k = b.CreateSubKey(path))
                k.SetValue(name, value, kind);
        }

        public static void Delete(string root, string path, string name)
        {
            using (RegistryKey b = Base(root))
            using (RegistryKey k = b.OpenSubKey(path, true))
                if (k != null) k.DeleteValue(name, false);
        }
    }

    // ---------------------------------------------------------------- Сведения о ПК
    class RamModule { public long Capacity; public int Speed; public int Configured; }

    static class SysInfo
    {
        public static int Build;
        public static bool IsAdmin;
        public static string CpuName = "";
        public static List<string> Gpus = new List<string>();
        public static bool HasNvidia, HasAmd;
        public static long VramBytes;
        public static List<RamModule> Ram = new List<RamModule>();
        public static bool HardwareLoaded;

        public static bool IsWin11 { get { return Build >= 22000; } }
        public static string OsName
        {
            get
            {
                string name = IsWin11 ? "Windows 11" : "Windows 10";
                object disp = Reg.Get("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion");
                return name + (disp != null ? " " + disp : "") + " (сборка " + Build + ")";
            }
        }

        public static void InitFast()
        {
            int b; object v = Reg.Get("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber");
            Build = v != null && int.TryParse(v.ToString(), out b) ? b : Environment.OSVersion.Version.Build;
            IsAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            object cpu = Reg.Get("HKLM", @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString");
            CpuName = cpu != null ? cpu.ToString().Trim() : "Процессор";
        }

        // WMI медленный — вызывается в фоне
        public static void LoadHardware()
        {
            List<string> gpus = new List<string>();
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                    foreach (ManagementObject o in s.Get())
                    {
                        string n = Convert.ToString(o["Name"]);
                        if (n.IndexOf("Basic Display", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Remote", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        gpus.Add(n);
                    }
            }
            catch { }
            List<RamModule> ram = new List<RamModule>();
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Capacity, Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory"))
                    foreach (ManagementObject o in s.Get())
                    {
                        RamModule m = new RamModule();
                        try { m.Capacity = Convert.ToInt64(o["Capacity"]); } catch { }
                        try { m.Speed = Convert.ToInt32(o["Speed"]); } catch { }
                        try { m.Configured = Convert.ToInt32(o["ConfiguredClockSpeed"]); } catch { }
                        ram.Add(m);
                    }
            }
            catch { }
            long vram = 0;
            try
            {
                using (RegistryKey b = Reg.Base("HKLM"))
                using (RegistryKey cls = b.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                    if (cls != null)
                        foreach (string sub in cls.GetSubKeyNames())
                        {
                            if (sub.Length != 4) continue;
                            try
                            {
                                using (RegistryKey k = cls.OpenSubKey(sub))
                                {
                                    if (k == null) continue;
                                    object q = k.GetValue("HardwareInformation.qwMemorySize");
                                    long val = 0;
                                    if (q is long) val = (long)q;
                                    else if (q is byte[] && ((byte[])q).Length >= 8) val = BitConverter.ToInt64((byte[])q, 0);
                                    else
                                    {
                                        object d = k.GetValue("HardwareInformation.MemorySize");
                                        if (d is int) val = (uint)(int)d;
                                        else if (d is byte[] && ((byte[])d).Length >= 4) val = BitConverter.ToUInt32((byte[])d, 0);
                                    }
                                    if (val > vram) vram = val;
                                }
                            }
                            catch { }
                        }
            }
            catch { }
            Gpus = gpus; Ram = ram; VramBytes = vram;
            foreach (string g in gpus)
            {
                if (g.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0) HasNvidia = true;
                if (g.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0 || g.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0) HasAmd = true;
            }
            HardwareLoaded = true;
        }

        public static string RamSummary()
        {
            if (Ram.Count == 0) return "—";
            long total = 0; int speed = 0, conf = 0;
            foreach (RamModule m in Ram) { total += m.Capacity; speed = Math.Max(speed, m.Speed); conf = Math.Max(conf, m.Configured); }
            string s = (total / (1024 * 1024 * 1024)) + " ГБ · " + Ram.Count + " шт.";
            int mhz = conf > 0 ? conf : speed;
            if (mhz > 0) s += " · " + mhz + " МГц";
            return s;
        }
    }
}
