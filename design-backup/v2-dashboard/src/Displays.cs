using System;
using System.Collections.Generic;
using System.Drawing;
using System.Management;
using System.Runtime.InteropServices;

namespace Tunex
{
    class MonitorInfo
    {
        public string Device, Name;
        public Rectangle Bounds; public bool Primary;
        public IntPtr Physical;
        public bool Ddc, HasContrast, Wmi;
        public int BMin, BMax = 100, BCur, CMin, CMax = 100, CCur;
        public int Hz; public List<int> Rates = new List<int>();
        public int MaxHz { get { int m = 0; foreach (int r in Rates) if (r > m) m = r; return m; } }
    }

    static class Displays
    {
        static readonly List<Native.PHYSICAL_MONITOR[]> opened = new List<Native.PHYSICAL_MONITOR[]>();

        public static void ReleaseAll()
        {
            foreach (Native.PHYSICAL_MONITOR[] a in opened) { try { Native.DestroyPhysicalMonitors((uint)a.Length, a); } catch { } }
            opened.Clear();
        }

        public static List<MonitorInfo> Enumerate(bool probeDdc)
        {
            if (probeDdc) ReleaseAll();
            List<MonitorInfo> list = new List<MonitorInfo>();
            Native.MonitorEnumProc cb = delegate(IntPtr hMon, IntPtr hdc, ref Native.RECT r, IntPtr data)
            {
                Native.MONITORINFOEX mi = new Native.MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFOEX));
                if (!Native.GetMonitorInfo(hMon, ref mi)) return true;
                MonitorInfo m = new MonitorInfo();
                m.Device = mi.szDevice;
                m.Bounds = Rectangle.FromLTRB(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom);
                m.Primary = (mi.dwFlags & 1) != 0;
                m.Name = FriendlyName(mi.szDevice);
                m.Hz = CurrentHz(m.Device);
                m.Rates = Rates(m.Device);
                if (probeDdc) ProbeDdc(hMon, m);
                list.Add(m);
                return true;
            };
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);
            GC.KeepAlive(cb);

            if (probeDdc)
            {
                // Ноутбук: встроенный экран управляется через WMI
                try
                {
                    using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness"))
                        foreach (ManagementObject o in s.Get())
                        {
                            MonitorInfo target = null;
                            foreach (MonitorInfo m in list) if (!m.Ddc && (target == null || m.Primary)) target = m;
                            if (target != null) { target.Wmi = true; target.BMin = 0; target.BMax = 100; target.BCur = Convert.ToInt32(o["CurrentBrightness"]); }
                            break;
                        }
                }
                catch { }
            }
            list.Sort(delegate(MonitorInfo a, MonitorInfo b) { return b.Primary.CompareTo(a.Primary); });
            return list;
        }

        static void ProbeDdc(IntPtr hMon, MonitorInfo m)
        {
            try
            {
                uint n;
                if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out n) || n == 0) return;
                Native.PHYSICAL_MONITOR[] arr = new Native.PHYSICAL_MONITOR[n];
                if (!Native.GetPhysicalMonitorsFromHMONITOR(hMon, n, arr)) return;
                opened.Add(arr);
                m.Physical = arr[0].hPhysicalMonitor;
                uint mn, cur, mx;
                if (Native.GetMonitorBrightness(m.Physical, out mn, out cur, out mx) && mx > mn)
                { m.Ddc = true; m.BMin = (int)mn; m.BMax = (int)mx; m.BCur = (int)cur; }
                if (Native.GetMonitorContrast(m.Physical, out mn, out cur, out mx) && mx > mn)
                { m.HasContrast = true; m.CMin = (int)mn; m.CMax = (int)mx; m.CCur = (int)cur; }
            }
            catch { }
        }

        static string FriendlyName(string device)
        {
            Native.DISPLAY_DEVICE dd = new Native.DISPLAY_DEVICE();
            dd.cb = Marshal.SizeOf(typeof(Native.DISPLAY_DEVICE));
            string name = null;
            if (Native.EnumDisplayDevices(device, 0, ref dd, 0)) name = dd.DeviceString;
            if (string.IsNullOrEmpty(name) || name.IndexOf("PnP", StringComparison.OrdinalIgnoreCase) >= 0 || name.StartsWith("Generic", StringComparison.OrdinalIgnoreCase))
                name = "Монитор " + device.Replace(@"\\.\DISPLAY", "");
            return name;
        }

        public static void SetBrightness(MonitorInfo m, int v)
        {
            if (m.Ddc) { Native.SetMonitorBrightness(m.Physical, (uint)v); return; }
            if (m.Wmi)
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods"))
                    foreach (ManagementObject o in s.Get()) { o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)v }); break; }
            }
        }

        public static void SetContrast(MonitorInfo m, int v) { if (m.HasContrast) Native.SetMonitorContrast(m.Physical, (uint)v); }

        public static bool SetGamma(string device, double gamma)
        {
            ushort[] ramp = new ushort[768];
            for (int i = 0; i < 256; i++)
            {
                double v = Math.Pow(i / 255.0, 1.0 / gamma) * 65535.0;
                ushort u = (ushort)Math.Max(0, Math.Min(65535, (int)Math.Round(v)));
                ramp[i] = ramp[256 + i] = ramp[512 + i] = u;
            }
            IntPtr dc = Native.CreateDC(null, device, null, IntPtr.Zero);
            if (dc == IntPtr.Zero) return false;
            try { return Native.SetDeviceGammaRamp(dc, ramp); }
            finally { Native.DeleteDC(dc); }
        }

        static Native.DEVMODE NewMode()
        {
            Native.DEVMODE dm = new Native.DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(typeof(Native.DEVMODE));
            return dm;
        }

        public static int CurrentHz(string device)
        {
            Native.DEVMODE dm = NewMode();
            return Native.EnumDisplaySettings(device, Native.ENUM_CURRENT_SETTINGS, ref dm) ? dm.dmDisplayFrequency : 0;
        }

        public static List<int> Rates(string device)
        {
            List<int> rates = new List<int>();
            Native.DEVMODE cur = NewMode();
            if (!Native.EnumDisplaySettings(device, Native.ENUM_CURRENT_SETTINGS, ref cur)) return rates;
            for (int i = 0; ; i++)
            {
                Native.DEVMODE dm = NewMode();
                if (!Native.EnumDisplaySettings(device, i, ref dm)) break;
                if (dm.dmPelsWidth == cur.dmPelsWidth && dm.dmPelsHeight == cur.dmPelsHeight && dm.dmBitsPerPel == cur.dmBitsPerPel
                    && dm.dmDisplayFrequency > 1 && !rates.Contains(dm.dmDisplayFrequency))
                    rates.Add(dm.dmDisplayFrequency);
            }
            rates.Sort();
            return rates;
        }

        public static void SetHz(string device, int hz)
        {
            Native.DEVMODE dm = NewMode();
            if (!Native.EnumDisplaySettings(device, Native.ENUM_CURRENT_SETTINGS, ref dm)) throw new Exception("не удалось прочитать режим экрана");
            dm.dmDisplayFrequency = hz;
            dm.dmFields = Native.DM_DISPLAYFREQUENCY;
            int r = Native.ChangeDisplaySettingsEx(device, ref dm, IntPtr.Zero, Native.CDS_UPDATEREGISTRY, IntPtr.Zero);
            if (r != 0 && r != 1) throw new Exception("Windows отклонила режим (код " + r + ")");
        }
    }
}
