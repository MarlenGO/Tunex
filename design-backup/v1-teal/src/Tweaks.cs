using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Tunex
{
    enum Risk { Safe, Moderate, Expert }

    // Насколько настройку стоит включать: All — всем (красная метка, пока выключена), Some — по ситуации (жёлтая), Optional — по желанию (серая)
    enum Recommend { All, Some, Optional }

    // Любая настройка: умеет прочитать своё состояние, запомнить «как было», применить и вернуть.
    abstract class Tweak
    {
        public string Id, Title, Description, Details, RecText, ConfirmText;
        public Recommend Rec = Recommend.Optional;
        public bool NeedsAdmin, NeedsReboot;
        public Risk Risk = Risk.Safe;
        public int MinBuild;
        public string UnavailableReason;

        public virtual bool IsAvailable()
        {
            if (MinBuild >= 22000 && SysInfo.Build < MinBuild) { UnavailableReason = "Только Windows 11"; return false; }
            if (MinBuild > 0 && SysInfo.Build < MinBuild) { UnavailableReason = "Нужна новее Windows"; return false; }
            return true;
        }
        public abstract bool ReadEnabled();
        public abstract string Capture();
        public abstract void Apply(bool on);
        public abstract void Restore(string snapshot);
    }

    // ---------------------------------------------------------------- Снимки реестра
    static class RegSnap
    {
        public static string Capture(IEnumerable<string[]> keys) // {root, path, name}
        {
            StringBuilder sb = new StringBuilder();
            foreach (string[] k in keys)
            {
                object v = Reg.Get(k[0], k[1], k[2]);
                RegistryValueKind kind = v == null ? RegistryValueKind.Unknown : Reg.Kind(k[0], k[1], k[2]);
                string data = "";
                if (v != null)
                {
                    if (kind == RegistryValueKind.DWord) data = Convert.ToInt32(v).ToString(CultureInfo.InvariantCulture);
                    else if (kind == RegistryValueKind.QWord) data = Convert.ToInt64(v).ToString(CultureInfo.InvariantCulture);
                    else if (kind == RegistryValueKind.Binary) data = Convert.ToBase64String((byte[])v);
                    else if (kind == RegistryValueKind.MultiString) data = string.Join("\u0001", (string[])v);
                    else data = Convert.ToString(v);
                }
                sb.Append(Esc(k[0])).Append('\t').Append(Esc(k[1])).Append('\t').Append(Esc(k[2])).Append('\t')
                  .Append(v == null ? "0" : "1").Append('\t').Append((int)kind).Append('\t').Append(Esc(data)).Append('\n');
            }
            return sb.ToString();
        }

        public static void Restore(string snap)
        {
            foreach (string line in snap.Split('\n'))
            {
                if (line.Length == 0) continue;
                string[] p = line.Split('\t');
                if (p.Length < 6) continue;
                string root = Un(p[0]), path = Un(p[1]), name = Un(p[2]), data = Un(p[5]);
                if (p[3] == "0") { Reg.Delete(root, path, name); continue; }
                RegistryValueKind kind = (RegistryValueKind)int.Parse(p[4]);
                object val;
                if (kind == RegistryValueKind.DWord) val = int.Parse(data, CultureInfo.InvariantCulture);
                else if (kind == RegistryValueKind.QWord) val = long.Parse(data, CultureInfo.InvariantCulture);
                else if (kind == RegistryValueKind.Binary) val = Convert.FromBase64String(data);
                else if (kind == RegistryValueKind.MultiString) val = data.Split('\u0001');
                else val = data;
                Reg.Set(root, path, name, val, kind);
            }
        }

        static string Esc(string s) { return Uri.EscapeDataString(s ?? ""); }
        static string Un(string s) { return Uri.UnescapeDataString(s); }
    }

    // ---------------------------------------------------------------- Типы настроек
    class RegSpec
    {
        public string Root, Path, Name; public RegistryValueKind Kind; public object On, Off, Default; // Default — как Windows ведёт себя, если значения нет
    }

    class RegistryTweak : Tweak
    {
        public readonly List<RegSpec> Specs = new List<RegSpec>();

        public RegistryTweak Dword(string root, string path, string name, int on, int? off, int? def = null)
        {
            RegSpec s = new RegSpec(); s.Root = root; s.Path = path; s.Name = name; s.Kind = RegistryValueKind.DWord; s.On = on;
            if (def.HasValue) s.Default = def.Value;
            s.Off = off.HasValue ? (object)off.Value : null;
            Specs.Add(s); return this;
        }

        public RegistryTweak Str(string root, string path, string name, string on, string off)
        {
            RegSpec s = new RegSpec(); s.Root = root; s.Path = path; s.Name = name; s.Kind = RegistryValueKind.String; s.On = on; s.Off = off;
            Specs.Add(s); return this;
        }

        static bool Same(object v, object want, RegistryValueKind kind)
        {
            if (v == null) return want == null;
            if (want == null) return false;
            if (kind == RegistryValueKind.DWord) { try { return Convert.ToInt32(v) == Convert.ToInt32(want); } catch { return false; } }
            return string.Equals(Convert.ToString(v), Convert.ToString(want), StringComparison.OrdinalIgnoreCase);
        }

        public override bool ReadEnabled()
        {
            foreach (RegSpec s in Specs) if (!Same(Reg.Get(s.Root, s.Path, s.Name) ?? s.Default, s.On, s.Kind)) return false;
            return true;
        }

        public override string Capture()
        {
            List<string[]> keys = new List<string[]>();
            foreach (RegSpec s in Specs) keys.Add(new string[] { s.Root, s.Path, s.Name });
            return RegSnap.Capture(keys);
        }

        public override void Apply(bool on)
        {
            foreach (RegSpec s in Specs)
            {
                object v = on ? s.On : s.Off;
                if (v == null) Reg.Delete(s.Root, s.Path, s.Name); else Reg.Set(s.Root, s.Path, s.Name, v, s.Kind);
            }
        }

        public override void Restore(string snapshot) { RegSnap.Restore(snapshot); }
    }

    // Ключ=значение внутри строки DirectXUserGlobalSettings (оптимизации для оконных игр и т.п.)
    class DxGlobalTweak : Tweak
    {
        const string Path = @"Software\Microsoft\DirectX\UserGpuPreferences", Name = "DirectXUserGlobalSettings";
        public string Key;

        static Dictionary<string, string> Parse(string s)
        {
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (s == null) return d;
            foreach (string part in s.Split(';'))
            {
                int i = part.IndexOf('=');
                if (i > 0) d[part.Substring(0, i).Trim()] = part.Substring(i + 1).Trim();
            }
            return d;
        }

        public override bool ReadEnabled()
        {
            Dictionary<string, string> d = Parse(Convert.ToString(Reg.Get("HKCU", Path, Name)));
            string v; return d.TryGetValue(Key, out v) && v == "1";
        }

        public override string Capture() { return RegSnap.Capture(new string[][] { new string[] { "HKCU", Path, Name } }); }

        public override void Apply(bool on)
        {
            Dictionary<string, string> d = Parse(Convert.ToString(Reg.Get("HKCU", Path, Name)));
            d[Key] = on ? "1" : "0";
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, string> kv in d) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
            Reg.Set("HKCU", Path, Name, sb.ToString(), RegistryValueKind.String);
        }

        public override void Restore(string snapshot) { RegSnap.Restore(snapshot); }
    }

    // Флаг совместимости для конкретного .exe (например, отключить оптимизацию во весь экран)
    class LayerFlagTweak : Tweak
    {
        const string Path = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
        public string Exe, Flag;

        List<string> Tokens()
        {
            List<string> t = new List<string>();
            string s = Convert.ToString(Reg.Get("HKCU", Path, Exe));
            foreach (string p in s.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) if (p != "~") t.Add(p);
            return t;
        }

        public override bool ReadEnabled() { return Tokens().Contains(Flag); }
        public override string Capture() { return RegSnap.Capture(new string[][] { new string[] { "HKCU", Path, Exe } }); }

        public override void Apply(bool on)
        {
            List<string> t = Tokens();
            t.Remove(Flag);
            if (on) t.Add(Flag);
            if (t.Count == 0) Reg.Delete("HKCU", Path, Exe);
            else Reg.Set("HKCU", Path, Exe, "~ " + string.Join(" ", t.ToArray()), RegistryValueKind.String);
        }

        public override void Restore(string snapshot) { RegSnap.Restore(snapshot); }
    }

    // Ускорение мыши («Повышенная точность установки указателя»)
    class MouseAccelTweak : Tweak
    {
        const int SPI_GETMOUSE = 3, SPI_SETMOUSE = 4, SPIF = 3;
        static int[] Get() { int[] m = new int[3]; Native.SystemParametersInfo(SPI_GETMOUSE, 0, m, 0); return m; }
        static void Set(int[] m) { if (!Native.SystemParametersInfo(SPI_SETMOUSE, 0, m, SPIF)) throw new Win32Exception(Marshal.GetLastWin32Error()); }

        public override bool ReadEnabled() { return Get()[2] == 0; }
        public override string Capture() { int[] m = Get(); return m[0] + "," + m[1] + "," + m[2]; }
        public override void Apply(bool on) { Set(on ? new int[] { 0, 0, 0 } : new int[] { 6, 10, 1 }); }
        public override void Restore(string s)
        {
            string[] p = s.Split(',');
            Set(new int[] { int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]) });
        }
    }

    // ---------------------------------------------------------------- Электропитание
    static class Power
    {
        public static readonly Guid Balanced = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e");
        public static readonly Guid High = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
        public static readonly Guid Saver = new Guid("a1841308-3541-4fab-bc81-f71556f20b4a");
        public static readonly Guid Ultimate = new Guid("e9a42b02-d5df-448d-aa00-03f14749eb61");

        public static Guid Active()
        {
            IntPtr p;
            if (Native.PowerGetActiveScheme(IntPtr.Zero, out p) != 0) return Guid.Empty;
            Guid g = (Guid)Marshal.PtrToStructure(p, typeof(Guid));
            Native.LocalFree(p);
            return g;
        }

        public static List<Guid> Schemes()
        {
            List<Guid> list = new List<Guid>();
            for (uint i = 0; i < 64; i++)
            {
                byte[] buf = new byte[16]; uint size = 16;
                if (Native.PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16, i, buf, ref size) != 0) break;
                list.Add(new Guid(buf));
            }
            return list;
        }

        public static string Name(Guid g)
        {
            uint size = 0;
            Native.PowerReadFriendlyName(IntPtr.Zero, ref g, IntPtr.Zero, IntPtr.Zero, null, ref size);
            if (size == 0) return "";
            byte[] b = new byte[size];
            if (Native.PowerReadFriendlyName(IntPtr.Zero, ref g, IntPtr.Zero, IntPtr.Zero, b, ref size) != 0) return "";
            return Encoding.Unicode.GetString(b).TrimEnd('\0');
        }

        public static void SetActive(Guid g) { Check(Native.PowerSetActiveScheme(IntPtr.Zero, ref g)); }

        public static Guid Duplicate(Guid src)
        {
            IntPtr dest = IntPtr.Zero;
            Check(Native.PowerDuplicateScheme(IntPtr.Zero, ref src, ref dest));
            Guid g = (Guid)Marshal.PtrToStructure(dest, typeof(Guid));
            Native.LocalFree(dest);
            return g;
        }

        public static uint ReadAC(Guid scheme, Guid sub, Guid setting)
        {
            uint v; Check(Native.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out v)); return v;
        }

        public static void WriteAC(Guid scheme, Guid sub, Guid setting, uint v)
        {
            Check(Native.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, v));
        }

        static void Check(uint r) { if (r != 0) throw new Win32Exception((int)r); }

        // ---- Своя схема «Максимальная производительность»
        public const string OwnName = "Максимальная производительность (Tunex)";
        static readonly Guid SubProcessor = new Guid("54533251-82be-4824-96c1-47b60b740d00");
        static readonly Guid SubDisk = new Guid("0012ee47-9041-4b5d-9b77-535fba8b1442");
        static readonly Guid SubPcie = new Guid("501a4d13-42af-4429-9fd1-a8218c268e20");
        static readonly Guid SubUsb = new Guid("2a737441-1930-4402-8d77-b2bebba308a3");

        public static bool IsOwn(Guid g, string name)
        {
            return g == Ultimate || name == OwnName || name.IndexOf("(Tunex)", StringComparison.Ordinal) >= 0;
        }

        public static void SetName(Guid g, string name)
        {
            byte[] b = Encoding.Unicode.GetBytes(name + "\0");
            Check(Native.PowerWriteFriendlyName(IntPtr.Zero, ref g, IntPtr.Zero, IntPtr.Zero, b, (uint)b.Length));
        }

        // Копия скрытой схемы Windows + явная запись ключевых значений.
        // На ноутбуках с современным режимом сна (S0) шаблон Windows пустой, поэтому значения пишем сами.
        public static Guid CreateMaxPerformance()
        {
            Guid g;
            try { g = Duplicate(Ultimate); }
            catch { try { g = Duplicate(High); } catch { g = Duplicate(Balanced); } }
            SetName(g, OwnName);
            ApplyMaxValues(g);
            return g;
        }

        public static void ApplyMaxValues(Guid g)
        {
            TryWrite(g, SubProcessor, new Guid("893dee8e-2bef-41e0-89c6-b55d0929964c"), 100); // минимальное состояние процессора
            TryWrite(g, SubProcessor, new Guid("bc5038f7-23e0-4960-96da-33abaf5935ec"), 100); // максимальное состояние процессора
            TryWrite(g, SubProcessor, new Guid("0cc5b647-c1df-4637-891a-dec35c318583"), 100); // парковка ядер: все активны
            TryWrite(g, SubProcessor, new Guid("be337238-0d82-4146-a960-4f3749d470c7"), 2);   // режим ускорения: агрессивный
            TryWrite(g, SubPcie, new Guid("ee12f906-d277-404b-b6da-e5fa1a576df5"), 0);         // PCI Express без экономии
            TryWrite(g, SubUsb, new Guid("48e6b7a6-50f5-4782-a5d4-53bb8f07e226"), 0);          // USB не засыпает
            TryWrite(g, SubDisk, new Guid("6738e2c4-e8a5-4a42-b16a-e040e769756e"), 0);         // диск не отключается
        }

        static void TryWrite(Guid scheme, Guid sub, Guid setting, uint v) { try { WriteAC(scheme, sub, setting, v); } catch { } }

        // Современный режим сна (Modern Standby, S0): Windows прячет дополнительные схемы в Панели управления
        public static bool IsModernStandby()
        {
            try
            {
                byte[] caps = new byte[128];
                if (Native.CallNtPowerInformation(4, IntPtr.Zero, 0, caps, (uint)caps.Length) != 0) return false;
                return caps[20] != 0; // SYSTEM_POWER_CAPABILITIES.AoAc
            }
            catch { return false; }
        }

        // ---- «Режим питания» (ползунок в Параметрах Windows)
        public static readonly Guid OverlayBest = new Guid("ded574b5-45a0-4f42-8737-46345c09c238");
        public static readonly Guid OverlayBalanced = Guid.Empty;
        public static readonly Guid OverlayBattery = new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a");

        public static bool OverlaySupported()
        {
            try { Guid g; return Native.PowerGetEffectiveOverlayScheme(out g) == 0; } catch { return false; }
        }

        public static Guid Overlay() { Guid g; Check(Native.PowerGetEffectiveOverlayScheme(out g)); return g; }
        public static void SetOverlay(Guid g) { Check(Native.PowerSetActiveOverlayScheme(ref g)); }

        public static string Describe(Guid g)
        {
            if (g == Balanced) return "Частоты меняются по нагрузке. Хороший выбор для большинства.";
            if (g == High) return "Процессор держит высокие частоты. Больше энергии и тепла.";
            if (g == Saver) return "Экономит энергию за счёт производительности. Для игр не подходит.";
            if (g == Ultimate) return "Без экономии энергии: процессор и устройства всегда на максимуме. Для игр от розетки.";
            return "Пользовательская или от производителя.";
        }
    }

    class PowerSettingTweak : Tweak
    {
        public Guid Sub, Setting; public uint OnValue, OffValue;

        public override bool IsAvailable()
        {
            try { Power.ReadAC(Power.Active(), Sub, Setting); return true; }
            catch { UnavailableReason = "Не поддерживается"; return false; }
        }
        public override bool ReadEnabled() { return Power.ReadAC(Power.Active(), Sub, Setting) == OnValue; }
        public override string Capture() { Guid s = Power.Active(); return s + "|" + Power.ReadAC(s, Sub, Setting); }
        public override void Apply(bool on)
        {
            Guid s = Power.Active();
            Power.WriteAC(s, Sub, Setting, on ? OnValue : OffValue);
            Power.SetActive(s);
        }
        public override void Restore(string snap)
        {
            string[] p = snap.Split('|');
            Guid s = new Guid(p[0]);
            Power.WriteAC(s, Sub, Setting, uint.Parse(p[1]));
            if (s == Power.Active()) Power.SetActive(s);
        }
    }

    // «Режим питания» из Параметров Windows — для журнала
    class OverlayTweak : Tweak
    {
        public OverlayTweak() { Id = "power.overlay"; Title = "Режим питания"; }
        public override bool ReadEnabled() { return false; }
        public override string Capture() { return Power.Overlay().ToString(); }
        public override void Apply(bool on) { }
        public override void Restore(string s) { Power.SetOverlay(new Guid(s)); }
    }

    // Не переключатель: используется для журнала (смена схемы питания)
    class PowerPlanTweak : Tweak
    {
        public PowerPlanTweak() { Id = "power.plan"; Title = "Схема питания"; }
        public override bool ReadEnabled() { return false; }
        public override string Capture() { return Power.Active().ToString(); }
        public override void Apply(bool on) { }
        public override void Restore(string s) { Power.SetActive(new Guid(s)); }
    }

    class RefreshTweak : Tweak
    {
        public string Device;
        public override bool ReadEnabled() { return false; }
        public override string Capture() { return Displays.CurrentHz(Device).ToString(); }
        public override void Apply(bool on) { }
        public override void Restore(string s) { Displays.SetHz(Device, int.Parse(s)); }
    }

    // ---------------------------------------------------------------- Новые типы настроек
    // Службы Windows: меняет тип запуска и сразу останавливает/запускает — без перезагрузки
    class ServiceTweak : Tweak
    {
        public readonly List<string> Services = new List<string>();
        public int StartOff = 3; // во что вернуть, если исходного снимка нет: 2 — автоматически, 3 — вручную

        public ServiceTweak() { NeedsAdmin = true; }

        List<string> Present()
        {
            List<string> l = new List<string>();
            foreach (string s in Services) if (ServiceTools.Exists(s)) l.Add(s);
            return l;
        }

        static string Key(string s) { return @"SYSTEM\CurrentControlSet\Services\" + s; }

        public override bool IsAvailable()
        {
            if (Present().Count == 0) { UnavailableReason = "Нет на этом ПК"; return false; }
            return true;
        }

        public override bool ReadEnabled()
        {
            foreach (string s in Present()) if (Convert.ToInt32(Reg.Get("HKLM", Key(s), "Start") ?? 3) != 4) return false;
            return true;
        }

        public override string Capture()
        {
            List<string[]> keys = new List<string[]>();
            foreach (string s in Present()) { keys.Add(new string[] { "HKLM", Key(s), "Start" }); keys.Add(new string[] { "HKLM", Key(s), "DelayedAutostart" }); }
            return RegSnap.Capture(keys);
        }

        public override void Apply(bool on)
        {
            foreach (string s in Present())
            {
                Reg.Set("HKLM", Key(s), "Start", on ? 4 : StartOff, RegistryValueKind.DWord);
                try { if (on) ServiceTools.Stop(s); else if (StartOff == 2) ServiceTools.Start(s); } catch { }
            }
        }

        public override void Restore(string snapshot)
        {
            RegSnap.Restore(snapshot);
            foreach (string s in Present())
            {
                int start = Convert.ToInt32(Reg.Get("HKLM", Key(s), "Start") ?? 3);
                try { if (start == 4) ServiceTools.Stop(s); else if (start == 2) ServiceTools.Start(s); } catch { }
            }
        }
    }

    // Энергосбережение проводной сети (Energy Efficient Ethernet) на всех адаптерах, которые его поддерживают
    class EeeTweak : Tweak
    {
        public override bool IsAvailable()
        {
            if (NetTools.EeeAdapters().Count == 0) { UnavailableReason = "Нет подходящей сетевой карты"; return false; }
            return true;
        }

        public override bool ReadEnabled()
        {
            List<NetAdapterInfo> a = NetTools.EeeAdapters();
            if (a.Count == 0) return false;
            foreach (NetAdapterInfo x in a) if (x.Eee != "0") return false;
            return true;
        }

        public override string Capture()
        {
            List<string[]> keys = new List<string[]>();
            foreach (NetAdapterInfo x in NetTools.EeeAdapters()) keys.Add(new string[] { "HKLM", x.KeyPath, "*EEE" });
            return RegSnap.Capture(keys);
        }

        public override void Apply(bool on)
        {
            foreach (NetAdapterInfo x in NetTools.EeeAdapters())
            {
                Reg.Set("HKLM", x.KeyPath, "*EEE", on ? "0" : "1", RegistryValueKind.String);
                if (!string.IsNullOrEmpty(x.Guid)) NetTools.Restart(x.Guid);
            }
        }

        public override void Restore(string snapshot)
        {
            RegSnap.Restore(snapshot);
            foreach (NetAdapterInfo x in NetTools.EeeAdapters()) if (!string.IsNullOrEmpty(x.Guid)) NetTools.Restart(x.Guid);
        }
    }

    class PagefileTweak : Tweak
    {
        public override bool ReadEnabled() { return Pagefile.IsAutomatic(); }
        public override string Capture() { return Pagefile.IsAutomatic() ? "1" : "0"; }
        public override void Apply(bool on) { Pagefile.SetAutomatic(on); }
        public override void Restore(string s) { Pagefile.SetAutomatic(s == "1"); }
    }

    // Настройка самой программы (например, «притормаживать фон во время игры»)
    class AppFlagTweak : Tweak
    {
        public string Flag;
        public override bool ReadEnabled() { return App.Manager != null && App.Manager.State.Flags.Contains(Flag); }
        public override string Capture() { return ReadEnabled() ? "1" : "0"; }
        public override void Apply(bool on)
        {
            List<string> f = App.Manager.State.Flags;
            f.Remove(Flag); if (on) f.Add(Flag);
        }
        public override void Restore(string s) { Apply(s == "1"); }
    }

    // ---------------------------------------------------------------- Каталог
    static class Catalog
    {
        const string GameBar = @"Software\Microsoft\GameBar";
        const string GameDvr = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
        const string GameCfg = @"System\GameConfigStore";
        const string Mm = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";

        static readonly Guid SubProcessor = new Guid("54533251-82be-4824-96c1-47b60b740d00");
        static readonly Guid ProcMin = new Guid("893dee8e-2bef-41e0-89c6-b55d0929964c");
        static readonly Guid CoreParking = new Guid("0cc5b647-c1df-4637-891a-dec35c318583");
        static readonly Guid SubPcie = new Guid("501a4d13-42af-4429-9fd1-a8218c268e20");
        static readonly Guid Aspm = new Guid("ee12f906-d277-404b-b6da-e5fa1a576df5");
        static readonly Guid SubUsb = new Guid("2a737441-1930-4402-8d77-b2bebba308a3");
        static readonly Guid UsbSuspend = new Guid("48e6b7a6-50f5-4782-a5d4-53bb8f07e226");

        public static readonly List<KeyValuePair<string, Tweak>> All = new List<KeyValuePair<string, Tweak>>();
        public static readonly PowerPlanTweak PowerPlan = new PowerPlanTweak();
        public static readonly OverlayTweak PowerOverlay = new OverlayTweak();

        static T Add<T>(string cat, T t) where T : Tweak { All.Add(new KeyValuePair<string, Tweak>(cat, t)); return t; }

        static RegistryTweak R(string id) { RegistryTweak t = new RegistryTweak(); t.Id = id; return t; }

        // Тексты для пользователя: заголовок, короткое описание, подробности, рекомендация
        static T Text<T>(T t, string title, string desc, string details, Recommend rec, string recText) where T : Tweak
        {
            t.Title = title; t.Description = desc; t.Details = details; t.Rec = rec; t.RecText = recText; return t;
        }

        static T Meta<T>(T t, bool admin, bool reboot) where T : Tweak { t.NeedsAdmin = admin; t.NeedsReboot = reboot; return t; }

        static ServiceTweak Svc(string id, int startOff, params string[] names)
        {
            ServiceTweak t = new ServiceTweak(); t.Id = id; t.StartOff = startOff; t.Services.AddRange(names); return t;
        }

        static PowerSettingTweak PS(string id, Guid sub, Guid setting, uint on, uint off)
        {
            PowerSettingTweak t = new PowerSettingTweak(); t.Id = id; t.Sub = sub; t.Setting = setting; t.OnValue = on; t.OffValue = off; return t;
        }

        public static void Build()
        {
            // ===== Игровой режим
            Add("game", Text(R("game.gamemode")
                .Dword("HKCU", GameBar, "AutoGameModeEnabled", 1, 0, 1)
                .Dword("HKCU", GameBar, "AllowAutoGameMode", 1, 0, 1),
                "Игровой режим", "Windows отдаёт игре больше ресурсов",
                "Пока вы играете, Windows не ставит обновления и меньше отвлекается на фоновые задачи.\nОбычно он включён, но его иногда выключают другие программы.",
                Recommend.All, "Рекомендуем всем"));

            Add("game", Text(R("game.gamebar")
                .Dword("HKCU", GameDvr, "AppCaptureEnabled", 0, 1)
                .Dword("HKCU", GameDvr, "HistoricalCaptureEnabled", 0, 1)
                .Dword("HKCU", GameCfg, "GameDVR_Enabled", 0, 1),
                "Отключить Xbox Game Bar", "Убирает фоновую запись игры, которая отнимает FPS",
                "Xbox Game Bar умеет постоянно записывать последние минуты игры. Это нагружает видеокарту и диск.\nПосле отключения Win+G перестанет открывать панель Xbox.",
                Recommend.All, "Рекомендуем, если не записываете клипы через Win+G"));

            RegistryTweak hags = R("game.hags").Dword("HKLM", @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2, 1);
            hags.MinBuild = 19041;
            Add("game", Meta(Text(hags,
                "Аппаратное планирование GPU", "Видеокарта сама распределяет свою работу",
                "Немного снижает задержку и нужно для генерации кадров (DLSS 3, FSR 3).\nНа старых видеокартах может, наоборот, давать рывки — тогда выключите.",
                Recommend.Some, "Для видеокарт GTX 1000 / RX 5000 и новее"), true, true));

            DxGlobalTweak win = new DxGlobalTweak();
            win.Id = "game.windowed"; win.Key = "SwapEffectUpgradeEnable"; win.MinBuild = 22000;
            Add("game", Text(win, "Оптимизации для оконных игр", "Игры в окне и «без рамки» работают плавнее",
                "Windows выводит картинку оконных игр так же быстро, как полноэкранных.\nЕсли после обновления Windows 11 24H2 FPS начал скакать — попробуйте выключить.",
                Recommend.Some, "Рекомендуем на Windows 11"));

            MouseAccelTweak mouse = new MouseAccelTweak(); mouse.Id = "game.mouse";
            Add("game", Text(mouse, "Отключить ускорение мыши", "Прицел двигается одинаково при любой скорости руки",
                "По умолчанию Windows разгоняет курсор, если резко дёрнуть мышь. В шутерах из-за этого сложно привыкнуть к прицеливанию.\nПосле отключения, возможно, придётся подстроить чувствительность.",
                Recommend.Some, "Рекомендуем для шутеров"));

            Add("game", Meta(Text(R("game.mpo").Dword("HKLM", @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode", 5, null),
                "Отключить MPO", "Убирает мерцание и рывки на некоторых видеокартах",
                "MPO — способ, которым Windows накладывает окна друг на друга. На части видеокарт NVIDIA из-за него мерцает экран и дёргается картинка.\nЕсли такой проблемы нет — не трогайте.",
                Recommend.Some, "Только если экран мерцает или картинка дёргается"), true, true));

            Add("game", Text(R("game.fse")
                .Dword("HKCU", GameCfg, "GameDVR_FSEBehaviorMode", 2, null)
                .Dword("HKCU", GameCfg, "GameDVR_HonorUserFSEBehaviorMode", 1, null)
                .Dword("HKCU", GameCfg, "GameDVR_FSEBehavior", 2, null)
                .Dword("HKCU", GameCfg, "GameDVR_DXGIHonorFSEWindowsCompatible", 1, null),
                "Отключить оптимизацию во весь экран", "«Честный» полноэкранный режим во всех играх",
                "Иногда снижает задержку, но может ломать Alt+Tab и оверлеи (Discord, Steam).\nЛучше включать это для конкретной игры в разделе «Игры».",
                Recommend.Optional, "Только если знаете, зачем это вам"));

            Add("game", Meta(Text(R("game.hvci").Dword("HKLM", @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled", 0, 1),
                "Отключить изоляцию ядра", "Может прибавить FPS, но снижает защиту Windows",
                "Эта защита не даёт вредоносным драйверам проникнуть в систему, и за это немного замедляет процессор.\nНекоторые античиты требуют, чтобы она была включена — иначе игра не запустится.",
                Recommend.Optional, "Не рекомендуем, если важна безопасность"), true, true));

            // ===== Электропитание
            Add("power", Text(PS("power.aspm", SubPcie, Aspm, 0, 1),
                "Отключить экономию PCI Express", "Видеокарта и SSD не «засыпают» во время игры",
                "Чтобы экономить энергию, Windows усыпляет видеокарту и быстрый SSD между запросами. Пробуждение занимает время и может давать редкие фризы.",
                Recommend.All, "Рекомендуем всем"));
            Add("power", Text(PS("power.usb", SubUsb, UsbSuspend, 0, 1),
                "Не усыплять USB-устройства", "Мышь и гарнитура не отваливаются и не тормозят",
                "Windows может отключать питание USB-портов в паузах. Из-за этого мышь или гарнитура иногда «просыпаются» с задержкой.",
                Recommend.All, "Рекомендуем всем"));
            Add("power", Text(PS("power.cpumin", SubProcessor, ProcMin, 100, 5),
                "Не сбрасывать частоту процессора", "Процессор всегда готов к нагрузке",
                "Процессор не уходит в экономичный режим между нагрузками — меньше микрофризов.\nНо в простое он будет теплее и шумнее. На ноутбуке действует только от розетки.",
                Recommend.Some, "Для настольных ПК"));
            Add("power", Text(PS("power.parking", SubProcessor, CoreParking, 100, 10),
                "Держать все ядра активными", "Windows не выключает «лишние» ядра процессора",
                "При слабой нагрузке Windows выключает часть ядер, чтобы экономить энергию. Когда игре они резко понадобятся, включение займёт время.",
                Recommend.Some, "Для настольных ПК"));

            // ===== Система
            Add("system", Text(R("system.bgapps").Dword("HKCU", @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1, 0),
                "Запретить фоновые приложения", "Приложения из Microsoft Store не работают в фоне",
                "Почта, погода, новости и другие приложения из магазина перестанут работать, когда они закрыты. Уведомления от них приходить не будут.",
                Recommend.All, "Рекомендуем всем"));

            const string Cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
            Add("system", Text(R("system.ads")
                .Dword("HKCU", Cdm, "SubscribedContent-338389Enabled", 0, 1)
                .Dword("HKCU", Cdm, "SubscribedContent-310093Enabled", 0, 1)
                .Dword("HKCU", Cdm, "SystemPaneSuggestionsEnabled", 0, 1)
                .Dword("HKCU", Cdm, "SoftLandingEnabled", 0, 1),
                "Отключить рекламу и советы Windows", "Меньше всплывающих подсказок и «рекомендаций»",
                "Windows перестанет показывать советы, предложения приложений и рекламу в Пуске и уведомлениях.",
                Recommend.All, "Рекомендуем всем"));

            Add("system", Text(Svc("system.diagtrack", 2, "DiagTrack"),
                "Отключить телеметрию", "Windows меньше отправляет данных в Microsoft",
                "Служба телеметрии собирает сведения о работе ПК и отправляет их в Microsoft. Иногда она заметно грузит диск и процессор.",
                Recommend.All, "Рекомендуем всем"));

            Add("system", Text(Svc("system.services", 3, "MapsBroker", "RetailDemo", "Fax", "RemoteRegistry", "WMPNetworkSvc", "dmwappushservice", "WerSvc"),
                "Отключить ненужные службы", "Факс, демо-режим магазина, офлайн-карты и прочее",
                "Отключаются службы, которые почти никому не нужны: факс, демо-режим для витрин, загрузка карт, удалённый реестр, отчёты об ошибках.\nНа игры и интернет это не влияет.",
                Recommend.All, "Рекомендуем всем"));

            PagefileTweak pf = new PagefileTweak(); pf.Id = "system.pagefile";
            Add("system", Meta(Text(pf,
                "Автоматический файл подкачки", "Защищает от вылетов, когда не хватает памяти",
                "Файл подкачки — запасная память на диске. Если его отключить или сделать слишком маленьким, игры могут вылетать.\nЛучше доверить его размер Windows.",
                Recommend.All, "Рекомендуем всем"), true, true));

            Add("system", Text(Svc("system.sysmain", 2, "SysMain"),
                "Отключить SysMain", "Windows перестанет заранее загружать программы",
                "Служба заранее подгружает в память программы, которыми вы часто пользуетесь. На быстрых SSD пользы почти нет, а диск иногда нагружается.\nЕсли игры стоят на обычном жёстком диске (HDD) — оставьте включённой.",
                Recommend.Some, "Если игры установлены на SSD"));

            Add("system", Text(R("system.transparency").Dword("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0, 1, 1),
                "Отключить прозрачность", "Немного разгружает видеокарту",
                "Полупрозрачные меню и панель задач станут обычными. Заметно только на слабых ПК и ноутбуках.",
                Recommend.Some, "Для слабых ПК"));

            Add("system", Meta(Text(R("system.mmcss")
                .Dword("HKLM", Mm + @"\Tasks\Games", "Priority", 6, 2)
                .Str("HKLM", Mm + @"\Tasks\Games", "Scheduling Category", "High", "Medium")
                .Str("HKLM", Mm + @"\Tasks\Games", "SFIO Priority", "High", "Normal"),
                "Повышенный приоритет игр", "Windows выделяет играм больше времени процессора",
                "Меняет настройки встроенного в Windows планировщика для игр. Эффект небольшой, но вреда тоже нет.",
                Recommend.Some, "Можно включить всем"), true, false));

            Add("system", Text(Svc("system.search", 2, "WSearch"),
                "Отключить индексирование поиска", "Меньше фоновой нагрузки на диск",
                "Windows перестанет составлять каталог файлов для поиска. Поиск в Пуске и Проводнике будет работать медленнее.",
                Recommend.Optional, "Только для слабых ПК с медленным диском"));

            // ===== Сеть
            EeeTweak eee = new EeeTweak(); eee.Id = "net.eee";
            eee.ConfirmText = "Сетевая карта перезапустится, и интернет пропадёт на несколько секунд. Продолжить?";
            Add("net", Meta(Text(eee, "Отключить энергосбережение сети", "Проводной интернет не «засыпает» — меньше скачков пинга",
                "Некоторые сетевые карты экономят энергию, на мгновение засыпая в паузах. В онлайн-играх это даёт скачки пинга.\nПри применении интернет пропадёт на несколько секунд.",
                Recommend.All, "Рекомендуем, если интернет по кабелю"), true, false));

            Add("net", Meta(Text(R("net.dopt").Dword("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0, null),
                "Не раздавать обновления другим ПК", "Windows не тратит ваш интернет на чужие компьютеры",
                "По умолчанию Windows может раздавать скачанные обновления другим людям через ваш интернет. Это отнимает скорость, особенно на слабом канале.",
                Recommend.All, "Рекомендуем всем"), true, false));

            Add("net", Meta(Text(R("net.throttling")
                .Dword("HKLM", Mm, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), 10)
                .Dword("HKLM", Mm, "SystemResponsiveness", 10, 20),
                "Снять ограничение сети для игр", "Популярный «твик против пинга»",
                "Убирает ограничение, которое Windows ставит сети во время воспроизведения звука и видео.\nРеальный эффект спорный: кому-то помогает, кому-то нет.",
                Recommend.Optional, "Эффект спорный — проверьте сами"), true, true));

            // ===== Во время игры (настройки самой программы)
            AppFlagTweak lower = new AppFlagTweak(); lower.Id = "session.lowerbg"; lower.Flag = "lowerbg";
            Add("session", Text(lower, "Притормаживать фон во время игры", "OneDrive, поиск и обновления уступают ресурсы игре",
                "Когда запущена игра из списка ниже, Tunex понижает приоритет фоновых процессов Windows. После выхода из игры всё возвращается.\nРаботает, пока Tunex открыт или свёрнут.",
                Recommend.All, "Рекомендуем всем"));
            AppFlagTweak mem = new AppFlagTweak(); mem.Id = "session.cleanmem"; mem.Flag = "cleanmem";
            Add("session", Meta(Text(mem, "Очищать память при запуске игры", "Освобождает оперативную память перед игрой",
                "Windows держит в памяти кэш недавно открытых файлов. Обычно это полезно, но в некоторых играх из-за этого бывают фризы.\nПрограммы при очистке не закрываются.",
                Recommend.Some, "Если памяти 16 ГБ или меньше"), true, false));
        }

        public static List<Tweak> InCategory(string cat)
        {
            List<Tweak> l = new List<Tweak>();
            foreach (KeyValuePair<string, Tweak> kv in All) if (kv.Key == cat) l.Add(kv.Value);
            return l;
        }

        // Настройки для конкретной игры: id = "тип|путь к exe"
        public static Tweak GameTweak(string kind, string exe)
        {
            string id = kind + "|" + exe;
            if (kind == "game.gpu")
                return Text(R(id).Str("HKCU", @"Software\Microsoft\DirectX\UserGpuPreferences", exe, "GpuPreference=2;", null),
                    "Мощная видеокарта", "Игра всегда запускается на дискретной видеокарте",
                    "На ноутбуках и ПК с двумя видеокартами Windows иногда запускает игру на слабой встроенной графике. Эта настройка это исключает.",
                    Recommend.All, "Рекомендуем всем");
            if (kind == "game.prio")
                return Meta(Text(R(id).Dword("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\" + Path.GetFileName(exe) + @"\PerfOptions", "CpuPriorityClass", 3, null),
                    "Высокий приоритет", "Процессор в первую очередь работает на игру",
                    "Windows будет каждый раз запускать игру с высоким приоритетом. Фоновые программы получат меньше времени процессора.",
                    Recommend.Some, "Полезно на слабых процессорах"), true, false);
            if (kind == "game.fso")
            {
                LayerFlagTweak t = new LayerFlagTweak(); t.Id = id; t.Exe = exe; t.Flag = "DISABLEDXMAXIMIZEDWINDOWEDMODE";
                return Text(t, "Честный полноэкранный режим", "Отключает оптимизацию во весь экран для этой игры",
                    "Иногда снижает задержку и убирает фризы. Но Alt+Tab может стать медленнее, а оверлеи — пропасть.",
                    Recommend.Optional, "Попробуйте, если в игре фризы");
            }
            return null;
        }

        public static Tweak Resolve(string id)
        {
            foreach (KeyValuePair<string, Tweak> kv in All) if (kv.Value.Id == id) return kv.Value;
            if (id == PowerPlan.Id) return PowerPlan;
            if (id == PowerOverlay.Id) return PowerOverlay;
            int bar = id.IndexOf('|');
            if (bar > 0)
            {
                string kind = id.Substring(0, bar), arg = id.Substring(bar + 1);
                if (kind == "display.hz") { RefreshTweak r = new RefreshTweak(); r.Id = id; r.Device = arg; r.Title = "Частота обновления"; return r; }
                return GameTweak(kind, arg);
            }
            return null;
        }
    }

    // ---------------------------------------------------------------- Состояние и журнал
    class Metrics
    {
        public float Cpu = -1, Ram = -1, Gpu = -1, GpuTemp = -1, CpuTemp = -1;
    }

    class JournalItem
    {
        public string TweakId, Title, Snapshot; public bool On; public string Note;
    }

    class JournalEntry
    {
        public string Id, Time, Title;
        public bool NeedsReboot, Undone;
        public List<JournalItem> Items = new List<JournalItem>();
        public Metrics Before, After;

        [ScriptIgnore] public DateTime When { get { return DateTime.Parse(Time, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); } }
    }

    class OriginalSnap { public string Snapshot; public bool WasOn; }

    class AppState
    {
        public Dictionary<string, OriginalSnap> Originals = new Dictionary<string, OriginalSnap>();
        public List<JournalEntry> Journal = new List<JournalEntry>();
        public List<string> Games = new List<string>();
        public List<string> Flags = new List<string>();
        public List<BenchResult> Benchmarks = new List<BenchResult>();
        public bool RestorePromptShown;

        public static string Dir
        {
            get
            {
                string over = Environment.GetEnvironmentVariable("TUNEX_STATE_DIR");
                return !string.IsNullOrEmpty(over) ? over : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Theme.AppName);
            }
        }
        static string FilePath { get { return Path.Combine(Dir, "state.json"); } }

        static JavaScriptSerializer Json() { JavaScriptSerializer j = new JavaScriptSerializer(); j.MaxJsonLength = int.MaxValue; return j; }

        public static AppState Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    AppState s = Json().Deserialize<AppState>(File.ReadAllText(FilePath, Encoding.UTF8));
                    if (s != null)
                    {
                        if (s.Originals == null) s.Originals = new Dictionary<string, OriginalSnap>();
                        if (s.Journal == null) s.Journal = new List<JournalEntry>();
                        if (s.Games == null) s.Games = new List<string>();
                        if (s.Flags == null) s.Flags = new List<string>();
                        if (s.Benchmarks == null) s.Benchmarks = new List<BenchResult>();
                        s.Benchmarks.RemoveAll(delegate(BenchResult b) { return b == null || b.Version != Bench.Version; }); // старая формула
                        return s;
                    }
                }
            }
            catch { }
            return new AppState();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, Json().Serialize(this), Encoding.UTF8);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null); else File.Move(tmp, FilePath);
            }
            catch { }
        }
    }

    // ---------------------------------------------------------------- Менеджер изменений
    class TweakManager
    {
        public const int ObserveSeconds = 60;
        public readonly AppState State;
        public readonly SensorService Sensors;
        readonly Dictionary<string, bool> pending = new Dictionary<string, bool>();
        public event EventHandler PendingChanged;
        public event EventHandler JournalChanged;

        public TweakManager(AppState state, SensorService sensors) { State = state; Sensors = sensors; }

        public int PendingCount { get { return pending.Count; } }

        public bool PendingNeedsReboot
        {
            get
            {
                foreach (string id in pending.Keys) { Tweak t = Catalog.Resolve(id); if (t != null && t.NeedsReboot) return true; }
                return false;
            }
        }

        public bool? PendingFor(string id) { bool v; if (pending.TryGetValue(id, out v)) return v; return null; }

        public void SetPending(Tweak t, bool on)
        {
            bool cur;
            try { cur = t.ReadEnabled(); } catch { cur = !on; }
            if (cur == on) pending.Remove(t.Id); else pending[t.Id] = on;
            if (PendingChanged != null) PendingChanged(this, EventArgs.Empty);
        }

        public void ClearPending()
        {
            pending.Clear();
            if (PendingChanged != null) PendingChanged(this, EventArgs.Empty);
        }

        static string Friendly(Exception ex)
        {
            if (ex is UnauthorizedAccessException || ex is System.Security.SecurityException) return "нет прав администратора";
            Win32Exception w = ex as Win32Exception;
            if (w != null && w.NativeErrorCode == 5) return "нет прав администратора";
            return ex.Message;
        }

        static bool SafeRead(Tweak t) { try { return t.ReadEnabled(); } catch { return false; } }

        void ApplyOne(Tweak t, bool on, JournalEntry entry)
        {
            string snap = t.Capture();
            bool wasOn = SafeRead(t);
            if (!State.Originals.ContainsKey(t.Id))
            {
                OriginalSnap o = new OriginalSnap(); o.Snapshot = snap; o.WasOn = wasOn;
                State.Originals[t.Id] = o;
            }
            if (on) t.Apply(true);
            else
            {
                OriginalSnap o;
                if (State.Originals.TryGetValue(t.Id, out o) && !o.WasOn) { t.Restore(o.Snapshot); State.Originals.Remove(t.Id); }
                else t.Apply(false);
            }
            JournalItem it = new JournalItem(); it.TweakId = t.Id; it.Title = t.Title; it.On = on; it.Snapshot = snap;
            entry.Items.Add(it);
            if (t.NeedsReboot) entry.NeedsReboot = true;
        }

        JournalEntry NewEntry()
        {
            JournalEntry e = new JournalEntry();
            e.Id = Guid.NewGuid().ToString("N");
            e.Time = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            return e;
        }

        public List<string> ApplyPending(out JournalEntry entry)
        {
            List<string> errors = new List<string>();
            entry = NewEntry();
            foreach (KeyValuePair<string, bool> kv in new List<KeyValuePair<string, bool>>(pending))
            {
                Tweak t = Catalog.Resolve(kv.Key);
                if (t == null) continue;
                try { ApplyOne(t, kv.Value, entry); }
                catch (Exception ex) { errors.Add(t.Title + ": " + Friendly(ex)); }
            }
            pending.Clear();
            entry.Title = entry.Items.Count == 1 ? entry.Items[0].Title : "Изменено настроек: " + entry.Items.Count;
            Commit(entry);
            if (PendingChanged != null) PendingChanged(this, EventArgs.Empty);
            return errors;
        }

        // Сразу применить одну настройку-переключатель (например, кнопка «Исправить» в проверке)
        public string ApplyToggleNow(Tweak t, bool on)
        {
            JournalEntry e = NewEntry();
            try { ApplyOne(t, on, e); }
            catch (Exception ex) { return Friendly(ex); }
            pending.Remove(t.Id);
            e.Title = t.Title;
            Commit(e);
            if (PendingChanged != null) PendingChanged(this, EventArgs.Empty);
            return null;
        }

        // Медленная часть (службы, сеть) — в фоновом потоке; журнал записывается потом из интерфейса
        public string ApplyToggleWork(Tweak t, bool on, out JournalEntry e)
        {
            e = NewEntry();
            try { ApplyOne(t, on, e); }
            catch (Exception ex) { return Friendly(ex); }
            e.Title = (on ? "Включено: " : "Выключено: ") + t.Title;
            return null;
        }

        public void CommitFromUi(JournalEntry e)
        {
            Commit(e);
            if (PendingChanged != null) PendingChanged(this, EventArgs.Empty);
        }

        // Действие без переключателя (схема питания, частота): снимок → действие → журнал
        public string ApplyAction(Tweak t, string title, Action action)
        {
            JournalEntry e = NewEntry();
            try
            {
                string snap = t.Capture();
                if (!State.Originals.ContainsKey(t.Id)) { OriginalSnap o = new OriginalSnap(); o.Snapshot = snap; State.Originals[t.Id] = o; }
                action();
                JournalItem it = new JournalItem(); it.TweakId = t.Id; it.Title = title; it.On = true; it.Snapshot = snap;
                e.Items.Add(it);
            }
            catch (Exception ex) { return Friendly(ex); }
            e.Title = title;
            Commit(e);
            return null;
        }

        void Commit(JournalEntry e)
        {
            if (e.Items.Count == 0) return;
            e.Before = Sensors.Average(DateTime.Now.AddSeconds(-ObserveSeconds), DateTime.Now);
            Sensors.AddMarker(e.Title);
            State.Journal.Insert(0, e);
            while (State.Journal.Count > 200) State.Journal.RemoveAt(State.Journal.Count - 1);
            State.Save();
            if (JournalChanged != null) JournalChanged(this, EventArgs.Empty);
        }

        public List<string> Undo(JournalEntry e)
        {
            List<string> errors = new List<string>();
            for (int i = e.Items.Count - 1; i >= 0; i--)
            {
                JournalItem it = e.Items[i];
                Tweak t = Catalog.Resolve(it.TweakId);
                if (t == null) continue;
                try { t.Restore(it.Snapshot); } catch (Exception ex) { errors.Add(it.Title + ": " + Friendly(ex)); }
            }
            e.Undone = true;
            State.Save();
            if (JournalChanged != null) JournalChanged(this, EventArgs.Empty);
            if (PendingChanged != null) PendingChanged(this, EventArgs.Empty);
            return errors;
        }

        // Вернуть всё, что программа когда-либо меняла, к состоянию до программы
        public List<string> RestoreAllOriginals(out bool reboot)
        {
            List<string> errors = new List<string>();
            reboot = false;
            foreach (KeyValuePair<string, OriginalSnap> kv in new List<KeyValuePair<string, OriginalSnap>>(State.Originals))
            {
                Tweak t = Catalog.Resolve(kv.Key);
                if (t == null) { State.Originals.Remove(kv.Key); continue; }
                try { t.Restore(kv.Value.Snapshot); State.Originals.Remove(kv.Key); if (t.NeedsReboot) reboot = true; }
                catch (Exception ex) { errors.Add(t.Title + ": " + Friendly(ex)); }
            }
            pending.Clear();
            State.Save();
            if (PendingChanged != null) PendingChanged(this, EventArgs.Empty);
            if (JournalChanged != null) JournalChanged(this, EventArgs.Empty);
            return errors;
        }

        public JournalEntry Observing
        {
            get
            {
                if (State.Journal.Count == 0) return null;
                JournalEntry e = State.Journal[0];
                return e.After == null && !e.Undone && (DateTime.Now - e.When).TotalSeconds < ObserveSeconds + 5 ? e : null;
            }
        }

        // Вызывается раз в секунду: завершает наблюдение «после»
        public void CheckObservations()
        {
            bool changed = false;
            foreach (JournalEntry e in State.Journal)
            {
                if (e.After != null) continue;
                double age = (DateTime.Now - e.When).TotalSeconds;
                if (age < ObserveSeconds) continue;
                if (age < ObserveSeconds + 30) e.After = Sensors.Average(e.When.AddSeconds(5), e.When.AddSeconds(ObserveSeconds));
                else e.After = new Metrics(); // программа была закрыта во время наблюдения
                changed = true;
            }
            if (changed)
            {
                State.Save();
                if (JournalChanged != null) JournalChanged(this, EventArgs.Empty);
            }
        }
    }

    // ---------------------------------------------------------------- Точка восстановления и очистка
    static class Maintenance
    {
        public static string CreateRestorePoint()
        {
            try
            {
                try { Reg.Set("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", "SystemRestorePointCreationFrequency", 0, RegistryValueKind.DWord); } catch { }
                ManagementScope scope = new ManagementScope(@"\\localhost\root\default");
                using (ManagementClass cls = new ManagementClass(scope, new ManagementPath("SystemRestore"), null))
                {
                    try
                    {
                        ManagementBaseObject pe = cls.GetMethodParameters("Enable");
                        pe["Drive"] = Path.GetPathRoot(Environment.SystemDirectory);
                        cls.InvokeMethod("Enable", pe, null);
                    }
                    catch { }
                    ManagementBaseObject p = cls.GetMethodParameters("CreateRestorePoint");
                    p["Description"] = Theme.AppName + ": перед изменениями";
                    p["RestorePointType"] = 12;
                    p["EventType"] = 100;
                    ManagementBaseObject r = cls.InvokeMethod("CreateRestorePoint", p, null);
                    uint code = Convert.ToUInt32(r["ReturnValue"]);
                    return code == 0 ? null : "код ошибки " + code + ". Проверьте, что включена «Защита системы» для диска C:";
                }
            }
            catch (Exception ex) { return ex.Message; }
        }

        public static long ClearShaderCache()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string low = local + "Low";
            string[] dirs =
            {
                Path.Combine(local, "D3DSCache"),
                Path.Combine(local, @"NVIDIA\DXCache"), Path.Combine(local, @"NVIDIA\GLCache"),
                Path.Combine(low, @"NVIDIA\PerDriverVersion\DXCache"), Path.Combine(low, @"NVIDIA\PerDriverVersion\GLCache"),
                Path.Combine(local, @"AMD\DxCache"), Path.Combine(local, @"AMD\DxcCache"), Path.Combine(local, @"AMD\VkCache"), Path.Combine(local, @"AMD\GLCache"),
                Path.Combine(low, @"Intel\ShaderCache")
            };
            long freed = 0;
            foreach (string d in dirs) freed += DeleteContents(d);
            return freed;
        }

        static long DeleteContents(string dir)
        {
            long freed = 0;
            if (!Directory.Exists(dir)) return 0;
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { long len = new FileInfo(f).Length; File.Delete(f); freed += len; } catch { }
                }
            }
            catch { }
            return freed;
        }
    }
}
