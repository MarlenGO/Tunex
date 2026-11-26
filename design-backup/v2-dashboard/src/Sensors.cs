using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Tunex
{
    class Sample
    {
        public DateTime T;
        public float Cpu = -1, Ram = -1, RamUsedGb, RamTotalGb;
        public float Gpu = -1, GpuTemp = -1, VramUsedGb = -1, VramTotalGb = -1, CpuTemp = -1;
        public bool VramShared;
    }

    // Датчики: опрос раз в секунду в фоновом потоке, история за 10 минут.
    class SensorService : IDisposable
    {
        public const int HistorySeconds = 600;
        readonly List<Sample> history = new List<Sample>();
        readonly List<KeyValuePair<DateTime, string>> markers = new List<KeyValuePair<DateTime, string>>();
        readonly object sync = new object();
        System.Threading.Timer timer;
        int busy;

        PerformanceCounter cpu;
        readonly Dictionary<string, PerformanceCounter> gpuEngines = new Dictionary<string, PerformanceCounter>();
        readonly List<PerformanceCounter> vramDedicated = new List<PerformanceCounter>();
        readonly List<PerformanceCounter> vramShared = new List<PerformanceCounter>();
        readonly List<PerformanceCounter> thermal = new List<PerformanceCounter>();
        DateTime lastEnum = DateTime.MinValue;
        bool inited;

        Process nvsmi;
        volatile string nvLine;

        public string GpuSource = "";
        public string CpuTempSource = "";

        public void Start()
        {
            timer = new System.Threading.Timer(Tick, null, 300, 1000);
        }

        void Init()
        {
            inited = true;
            try { cpu = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total"); cpu.NextValue(); }
            catch
            {
                try { cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total"); cpu.NextValue(); } catch { cpu = null; }
            }
            try
            {
                if (PerformanceCounterCategory.Exists("Thermal Zone Information"))
                {
                    PerformanceCounterCategory cat = new PerformanceCounterCategory("Thermal Zone Information");
                    foreach (string inst in cat.GetInstanceNames())
                    {
                        try { PerformanceCounter pc = new PerformanceCounter("Thermal Zone Information", "Temperature", inst); pc.NextValue(); thermal.Add(pc); } catch { }
                    }
                    if (thermal.Count > 0) CpuTempSource = "датчик ACPI (примерно)";
                }
            }
            catch { }
            StartNvSmi();
        }

        void StartNvSmi()
        {
            string[] paths =
            {
                Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"NVIDIA Corporation\NVSMI\nvidia-smi.exe")
            };
            foreach (string p in paths)
            {
                if (!File.Exists(p)) continue;
                try
                {
                    ProcessStartInfo si = new ProcessStartInfo(p, "--query-gpu=temperature.gpu,utilization.gpu,memory.used,memory.total --format=csv,noheader,nounits -lms 1000");
                    si.UseShellExecute = false; si.CreateNoWindow = true; si.RedirectStandardOutput = true;
                    nvsmi = Process.Start(si);
                    nvsmi.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (!string.IsNullOrEmpty(e.Data)) nvLine = e.Data; };
                    nvsmi.BeginOutputReadLine();
                    GpuSource = "nvidia-smi";
                }
                catch { nvsmi = null; }
                break;
            }
        }

        void EnumerateGpuCounters()
        {
            lastEnum = DateTime.Now;
            try
            {
                if (PerformanceCounterCategory.Exists("GPU Engine"))
                {
                    string[] names = new PerformanceCounterCategory("GPU Engine").GetInstanceNames();
                    HashSet<string> wanted = new HashSet<string>();
                    foreach (string n in names) if (n.IndexOf("engtype_3D", StringComparison.OrdinalIgnoreCase) >= 0) wanted.Add(n);
                    foreach (string n in new List<string>(gpuEngines.Keys))
                        if (!wanted.Contains(n)) { gpuEngines[n].Dispose(); gpuEngines.Remove(n); }
                    foreach (string n in wanted)
                        if (!gpuEngines.ContainsKey(n))
                        {
                            try { PerformanceCounter pc = new PerformanceCounter("GPU Engine", "Utilization Percentage", n); pc.NextValue(); gpuEngines[n] = pc; } catch { }
                        }
                    if (GpuSource == "") GpuSource = "счётчики Windows";
                }
                if (vramDedicated.Count == 0 && PerformanceCounterCategory.Exists("GPU Adapter Memory"))
                {
                    foreach (string n in new PerformanceCounterCategory("GPU Adapter Memory").GetInstanceNames())
                    {
                        try { vramDedicated.Add(new PerformanceCounter("GPU Adapter Memory", "Dedicated Usage", n)); } catch { }
                        try { vramShared.Add(new PerformanceCounter("GPU Adapter Memory", "Shared Usage", n)); } catch { }
                    }
                }
            }
            catch { }
        }

        void Tick(object state)
        {
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            try
            {
                if (!inited) Init();
                if ((DateTime.Now - lastEnum).TotalSeconds > 10) EnumerateGpuCounters();

                Sample s = new Sample(); s.T = DateTime.Now;
                if (cpu != null) { try { s.Cpu = Math.Min(100f, cpu.NextValue()); } catch { } }

                Native.MEMORYSTATUSEX m = new Native.MEMORYSTATUSEX();
                if (Native.GlobalMemoryStatusEx(m))
                {
                    s.RamTotalGb = m.ullTotalPhys / 1073741824f;
                    s.RamUsedGb = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824f;
                    s.Ram = s.RamUsedGb / s.RamTotalGb * 100f;
                }

                if (gpuEngines.Count > 0)
                {
                    float sum = 0;
                    foreach (PerformanceCounter pc in gpuEngines.Values) { try { sum += pc.NextValue(); } catch { } }
                    s.Gpu = Math.Min(100f, sum);
                }

                long ded = 0, sh = 0;
                foreach (PerformanceCounter pc in vramDedicated) { try { ded += (long)pc.NextValue(); } catch { } }
                foreach (PerformanceCounter pc in vramShared) { try { sh += (long)pc.NextValue(); } catch { } }
                if (SysInfo.VramBytes >= 1L << 30)
                {
                    s.VramUsedGb = ded / 1073741824f; s.VramTotalGb = SysInfo.VramBytes / 1073741824f;
                }
                else if (vramShared.Count > 0)
                {
                    s.VramShared = true; s.VramUsedGb = (ded + sh) / 1073741824f; s.VramTotalGb = s.RamTotalGb / 2f;
                }

                float t = -1;
                foreach (PerformanceCounter pc in thermal)
                {
                    try { float k = pc.NextValue(); float c = k - 273.15f; if (c > 5 && c < 115 && c > t) t = c; } catch { }
                }
                s.CpuTemp = t;

                string line = nvLine;
                if (line != null)
                {
                    string[] p = line.Split(',');
                    float v;
                    if (p.Length >= 4)
                    {
                        if (TryF(p[0], out v)) s.GpuTemp = v;
                        if (TryF(p[1], out v)) s.Gpu = v;
                        float used, total;
                        if (TryF(p[2], out used) && TryF(p[3], out total) && total > 0) { s.VramUsedGb = used / 1024f; s.VramTotalGb = total / 1024f; s.VramShared = false; }
                    }
                }

                lock (sync)
                {
                    history.Add(s);
                    while (history.Count > HistorySeconds) history.RemoveAt(0);
                    while (markers.Count > 0 && (DateTime.Now - markers[0].Key).TotalSeconds > HistorySeconds) markers.RemoveAt(0);
                }
            }
            catch { }
            finally { Interlocked.Exchange(ref busy, 0); }
        }

        static bool TryF(string s, out float v)
        {
            return float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        public Sample Latest
        {
            get { lock (sync) return history.Count > 0 ? history[history.Count - 1] : null; }
        }

        public List<Sample> Recent(int seconds)
        {
            lock (sync)
            {
                int n = Math.Min(seconds, history.Count);
                return history.GetRange(history.Count - n, n);
            }
        }

        public List<KeyValuePair<DateTime, string>> Markers
        {
            get { lock (sync) return new List<KeyValuePair<DateTime, string>>(markers); }
        }

        public void AddMarker(string label) { lock (sync) markers.Add(new KeyValuePair<DateTime, string>(DateTime.Now, label)); }

        public Metrics Average(DateTime from, DateTime to)
        {
            Metrics r = new Metrics();
            double cpu = 0, ram = 0, gpu = 0, gt = 0, ct = 0; int nc = 0, nr = 0, ng = 0, ngt = 0, nct = 0;
            lock (sync)
            {
                foreach (Sample s in history)
                {
                    if (s.T < from || s.T > to) continue;
                    if (s.Cpu >= 0) { cpu += s.Cpu; nc++; }
                    if (s.Ram >= 0) { ram += s.Ram; nr++; }
                    if (s.Gpu >= 0) { gpu += s.Gpu; ng++; }
                    if (s.GpuTemp >= 0) { gt += s.GpuTemp; ngt++; }
                    if (s.CpuTemp >= 0) { ct += s.CpuTemp; nct++; }
                }
            }
            if (nc > 0) r.Cpu = (float)(cpu / nc);
            if (nr > 0) r.Ram = (float)(ram / nr);
            if (ng > 0) r.Gpu = (float)(gpu / ng);
            if (ngt > 0) r.GpuTemp = (float)(gt / ngt);
            if (nct > 0) r.CpuTemp = (float)(ct / nct);
            return r;
        }

        public void Dispose()
        {
            if (timer != null) timer.Dispose();
            try { if (nvsmi != null && !nvsmi.HasExited) nvsmi.Kill(); } catch { }
        }
    }
}
