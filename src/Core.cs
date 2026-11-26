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

        // Дизайн v3 «Стекло» (GlassWise UI): светлая и тёмная тема в духе Apple, мягкое голубое свечение
        // в левом верхнем углу, почти белые (в тёмной — полупрозрачные) карточки, круглые кнопки, системные цвета iOS.
        // Прежние дизайны — design-backup\v2-dashboard и design-backup\v1-teal
        public static bool Dark;
        public static Color Bg, Glow, Sidebar, SidebarGlow, Card, CardInner, CardHover, Border, CardEdge, Frame, Pop, PopHover, Field, FieldEdge, Seg, Scrim;
        // Акцент меняется в «Оформлении»; оттенки (фон выбранного пункта, текст на кнопке) считаются из него
        public static Color Accent, AccentSoft, AccentText, OnAccent = Color.White;

        public static readonly string[] PresetNames = { "Синий", "Индиго", "Бирюзовый" };
        static readonly Color[] PresetsLight = { Color.FromArgb(0, 122, 255), Color.FromArgb(88, 86, 214), Color.FromArgb(48, 176, 199) };
        static readonly Color[] PresetsDark = { Color.FromArgb(10, 132, 255), Color.FromArgb(94, 92, 230), Color.FromArgb(64, 200, 224) };
        public static Color[] Presets { get { return Dark ? PresetsDark : PresetsLight; } }
        public static Color DefaultAccent { get { return Presets[0]; } }

        public static Color Green, Blue, Orange, Red, Purple, Text, Text2, Text3, ToggleOff, Button, ButtonBorder;

        // Тема: "system" — как в Windows, "light" — светлая, "dark" — тёмная
        public static string Mode = "system";

        public static bool SystemDark()
        {
            try
            {
                object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
                return v is int && (int)v == 0;
            }
            catch { return false; }
        }

        public static void SetMode(string mode)
        {
            Mode = mode == "light" || mode == "dark" ? mode : "system";
            Dark = Mode == "dark" || (Mode == "system" && SystemDark());
            if (Dark)
            {
                Bg = Color.FromArgb(11, 13, 20); Glow = Color.FromArgb(22, 48, 97);
                Card = Color.FromArgb(17, 255, 255, 255); CardEdge = Color.FromArgb(22, 255, 255, 255);
                CardInner = Color.FromArgb(20, 255, 255, 255); CardHover = Color.FromArgb(36, 255, 255, 255);
                Border = Color.FromArgb(26, 255, 255, 255); Frame = Color.FromArgb(38, 42, 54);
                Pop = Color.FromArgb(26, 32, 48); PopHover = Color.FromArgb(44, 50, 66);
                Field = Color.FromArgb(8, 9, 14); FieldEdge = Color.FromArgb(23, 255, 255, 255);
                Seg = Color.FromArgb(46, 255, 255, 255); Scrim = Color.FromArgb(170, 0, 0, 0);
                Text = Color.White; Text2 = Color.FromArgb(170, 175, 189); Text3 = Color.FromArgb(122, 127, 140);
                Green = Color.FromArgb(48, 209, 88); Blue = Color.FromArgb(64, 200, 224); Orange = Color.FromArgb(255, 159, 10);
                Red = Color.FromArgb(255, 69, 58); Purple = Color.FromArgb(191, 90, 242);
            }
            else
            {
                Bg = Color.FromArgb(242, 244, 248); Glow = Color.FromArgb(207, 224, 255);
                Card = Color.FromArgb(240, 255, 255, 255); CardEdge = Color.FromArgb(26, 16, 32, 64);
                CardInner = Color.FromArgb(18, 16, 32, 64); CardHover = Color.FromArgb(31, 16, 32, 64);
                Border = Color.FromArgb(31, 16, 32, 64); Frame = Color.FromArgb(206, 212, 224);
                Pop = Color.White; PopHover = Color.FromArgb(238, 240, 244);
                Field = Color.FromArgb(241, 243, 247); FieldEdge = Color.FromArgb(31, 16, 32, 64);
                Seg = Color.White; Scrim = Color.FromArgb(92, 0, 0, 0);
                Text = Color.FromArgb(11, 18, 32); Text2 = Color.FromArgb(88, 95, 108); Text3 = Color.FromArgb(132, 138, 150);
                Green = Color.FromArgb(52, 199, 89); Blue = Color.FromArgb(48, 176, 199); Orange = Color.FromArgb(255, 149, 0);
                Red = Color.FromArgb(255, 59, 48); Purple = Color.FromArgb(175, 82, 222);
            }
            Sidebar = Bg; SidebarGlow = Glow;
            ToggleOff = CardHover; Button = CardHover; ButtonBorder = Color.Transparent;
            SetAccent(Accent.IsEmpty ? DefaultAccent : MatchPreset(Accent));
        }

        // Готовый цвет другой темы — тот же цвет в текущей; акценты прежних дизайнов — синий по умолчанию
        public static Color MatchPreset(Color c)
        {
            for (int i = 0; i < PresetsLight.Length; i++)
                if (PresetsLight[i].ToArgb() == c.ToArgb() || PresetsDark[i].ToArgb() == c.ToArgb()) return Presets[i];
            string h = ToHex(c);
            if (h == "#C6F05C" || h == "#4DC9B0" || h == "#5C98FF") return DefaultAccent;
            return c;
        }

        public static Color OnAccentFor(Color a) { return Luma(a) > 0.62f ? Blend(a, Color.Black, 0.85f) : Color.White; }
        public static float Luma(Color c) { return (0.299f * c.R + 0.587f * c.G + 0.114f * c.B) / 255f; }

        public static void SetAccent(Color a)
        {
            a = Color.FromArgb(255, a.R, a.G, a.B);
            // Слишком тёмный цвет на тёмном фоне (или слишком светлый на светлом) плохо видно — поправляем
            for (int i = 0; i < 8 && Dark && Luma(a) < 0.38f; i++) a = Blend(a, Color.White, 0.15f);
            for (int i = 0; i < 8 && !Dark && Luma(a) > 0.72f; i++) a = Blend(a, Color.Black, 0.12f);
            Accent = a;
            AccentSoft = Alpha(a, 36);   // --tint: мягкий фон выбранного пункта
            AccentText = a;
            OnAccent = OnAccentFor(a);
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

        // Скругления как в GlassWise: карточки 12–14 px, кнопки круглые — ограничения больше нет
        public static float MaxRadius { get { return Sf(400); } }

        // Подписи — обычными буквами (в прошлом дизайне были заглавные с разрядкой)
        public static string Track(string s) { return s; }

        // Фон окна: цвет и голубое свечение в левом верхнем углу окна (как --page-bg), общее для всех частей окна
        public static void PaintBackdrop(Graphics g, Control c, Rectangle area)
        {
            using (SolidBrush b = new SolidBrush(Bg)) g.FillRectangle(b, area);
            Form f = c.FindForm();
            if (f == null || !c.IsHandleCreated) return;
            Point o = f.PointToClient(c.PointToScreen(Point.Empty));
            float rx = f.ClientSize.Width * 0.63f, ry = f.ClientSize.Height * 0.32f;
            if (rx < 2 || ry < 2) return;
            RectangleF glow = new RectangleF(-rx - o.X, -ry - o.Y, rx * 2, ry * 2);
            if (!glow.IntersectsWith(area)) return;
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddEllipse(glow);
                using (PathGradientBrush pb = new PathGradientBrush(p))
                {
                    pb.CenterPoint = new PointF(-o.X, -o.Y);
                    pb.CenterColor = Glow;
                    pb.SurroundColors = new Color[] { Alpha(Glow, 0) };
                    g.FillPath(pb, p);
                }
            }
        }

        // Карточка: заливка и тонкая кромка
        public static void PaintCard(Graphics g, RectangleF r, float rad)
        {
            FillRound(g, Card, r, rad);
            DrawRound(g, CardEdge, r, rad, 1f);
        }

        public static Font FontBody, FontBodyBold, FontSmall, FontSmallBold, FontNav, FontTitle, FontH2, FontBig, FontIcon, FontIconBig, FontIconSmall, FontIconTiny, FontIconNav, FontLogo, FontLabel, FontNum, FontCellTitle;

        public static void Init()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96f;
            if (Accent.IsEmpty) SetMode(Mode);
            FontBody = new Font("Segoe UI", 9.75f);
            FontBodyBold = new Font("Segoe UI Semibold", 9.75f);
            FontCellTitle = new Font("Segoe UI Semibold", 11.25f);
            FontSmall = new Font("Segoe UI", 8.5f);
            FontNav = new Font("Segoe UI Semibold", 9.75f);
            // GlassWise: системный шрифт Segoe UI; заголовки жирные (28 px / 20 px), подписи разделов 13 px полужирные
            FontSmallBold = new Font("Segoe UI Semibold", 8.5f);
            FontTitle = new Font("Segoe UI", 20f, FontStyle.Bold);
            FontH2 = new Font("Segoe UI", 11.25f, FontStyle.Bold);
            FontBig = new Font("Segoe UI Semibold", 20f);
            FontLogo = new Font("Segoe UI", 11.25f, FontStyle.Bold);
            FontLabel = new Font("Segoe UI Semibold", 9f);
            FontNum = new Font("Segoe UI", 9.75f);
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
        public const string Sun = "\uE706";
        public const string Moon = "\uE708";
        public const string Search = "\uE721";
        public const string Fps = "\uE9D2";
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
