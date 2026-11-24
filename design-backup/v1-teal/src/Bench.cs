using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Tunex
{
    class BenchResult
    {
        public string Time;
        public int Version;                       // результаты старой формулы не сравниваем
        public int CpuSingle, CpuMulti, Cpu, GpuScore, RamScore, DiskScore, Total;
        public bool GpuIntegrated, GpuKnown;
        public double RamGBps, DiskReadMBps, DiskWriteMBps, CpuSingleRaw, CpuMultiRaw;
        public string CpuName, GpuName;
        public int Threads;
        public DateTime When { get { return DateTime.Parse(Time, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); } }
    }

    // Игровой рейтинг: процессор, память и диск замеряются, видеокарта оценивается по модели.
    // 1000 очков — сильный игровой ПК (уровень RTX 4070 + современный 6–8-ядерный процессор, NVMe).
    // Без отдельной видеокарты рейтинг низкий, даже если процессор быстрый — для игр важнее всего видеокарта.
    static class Bench
    {
        public const int Version = 2;
        const double RefSingle = 650, RefMulti = 9000;     // млн итераций в секунду (i5-12500H даёт ≈ 500 / 4800)
        const double RefRamGBps = 10, RefDiskMBps = 3000;

        public static string Tier(int score)
        {
            if (score <= 0) return "—";
            if (score < 450) return "Слабый";
            if (score < 700) return "Средний";
            if (score < 1000) return "Хороший";
            return "Мощный";
        }

        // Короткий вывод простыми словами
        public static string Verdict(BenchResult r)
        {
            if (r.GpuIntegrated)
                return "Нет отдельной видеокарты — новые игры пойдут только на низких настройках, лёгкие (CS2, Dota 2, Minecraft) — нормально. Для работы и учёбы мощности хватает.";
            if (r.GpuScore < 450) return "Видеокарта начального уровня: большинство игр на низких–средних настройках в Full HD.";
            if (r.GpuScore < 750) return "Хватает для большинства игр на средних–высоких настройках в Full HD.";
            if (r.GpuScore < 1100) return "Современные игры на высоких настройках в Full HD и 2K.";
            return "Топовый уровень: высокие настройки в 2K и 4K.";
        }

        public static BenchResult Run(Action<string, int> progress, Func<bool> cancelled)
        {
            BenchResult r = new BenchResult();
            r.Time = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            r.CpuName = SysInfo.CpuName;
            r.GpuName = SysInfo.Gpus.Count > 0 ? SysInfo.Gpus[0] : "";
            r.Threads = Environment.ProcessorCount;
            Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;

            progress("Процессор: одно ядро", 5);
            Warmup();
            double single = CpuRate(1, 3000);
            r.CpuSingleRaw = single; r.CpuSingle = Score(single, RefSingle);
            if (cancelled()) return null;

            progress("Процессор: все ядра", 30);
            double multi = CpuRate(Environment.ProcessorCount, 4000);
            r.CpuMultiRaw = multi; r.CpuMulti = Score(multi, RefMulti);
            if (cancelled()) return null;

            progress("Оперативная память", 60);
            r.RamGBps = RamCopy(2500);
            r.RamScore = Score(r.RamGBps, RefRamGBps);
            if (cancelled()) return null;

            progress("Диск", 75);
            try
            {
                double w, rd;
                DiskSpeed(out w, out rd);
                r.DiskWriteMBps = w; r.DiskReadMBps = rd;
                r.DiskScore = Score(0.3 * w + 0.7 * rd, RefDiskMBps); // для игр важнее чтение
            }
            catch { r.DiskScore = 0; }

            bool known, integrated;
            for (int i = 0; i < 50 && !SysInfo.HardwareLoaded; i++) Thread.Sleep(100);
            r.GpuScore = GpuDb.Score(out known, out integrated);
            r.GpuKnown = known; r.GpuIntegrated = integrated;
            r.Cpu = (int)Math.Round(r.CpuSingle * 0.6 + r.CpuMulti * 0.4);
            r.Total = (int)Math.Round(r.GpuScore * 0.45 + r.Cpu * 0.35 + r.RamScore * 0.1 + r.DiskScore * 0.1);
            r.Version = Version;
            progress("Готово", 100);
            return r;
        }

        static int Score(double value, double reference) { return (int)Math.Round(Math.Min(value / reference, 2.5) * 1000); }

        // ---- Процессор: целочисленная и вещественная арифметика, без обращений к памяти
        static ulong Chunk(ulong seed)
        {
            ulong x = seed | 1; double d = 1.0;
            for (int i = 0; i < 1000000; i++)
            {
                x ^= x << 13; x ^= x >> 7; x ^= x << 17;
                d = d * 1.0000001 + (x & 1023) * 1e-9;
            }
            return x ^ (ulong)d;
        }

        static void Warmup() { Chunk(1); Chunk(2); }

        static long sink;

        // Возвращает миллионы итераций в секунду на всех потоках вместе
        static double CpuRate(int threads, int ms)
        {
            long total = 0;
            Stopwatch sw = Stopwatch.StartNew();
            Thread[] ts = new Thread[threads];
            for (int t = 0; t < threads; t++)
            {
                int id = t;
                ts[t] = new Thread(delegate()
                {
                    long n = 0; ulong acc = 0;
                    while (sw.ElapsedMilliseconds < ms) { acc += Chunk((ulong)(id * 7919 + n)); n++; }
                    Interlocked.Add(ref total, n);
                    Interlocked.Add(ref sink, (long)(acc & 1));
                });
                ts[t].IsBackground = true;
                ts[t].Priority = ThreadPriority.AboveNormal;
                ts[t].Start();
            }
            foreach (Thread t in ts) t.Join();
            return total / (sw.Elapsed.TotalSeconds);
        }

        // ---- Память: скорость копирования больших блоков, ГБ/с
        static double RamCopy(int ms)
        {
            const int Size = 128 << 20;
            byte[] a = new byte[Size], b = new byte[Size];
            for (int i = 0; i < Size; i += 4096) a[i] = (byte)i;
            Buffer.BlockCopy(a, 0, b, 0, Size);
            long bytes = 0;
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                Buffer.BlockCopy(a, 0, b, 0, Size);
                Buffer.BlockCopy(b, 0, a, 0, Size);
                bytes += 2L * Size;
            }
            return bytes / sw.Elapsed.TotalSeconds / 1e9;
        }

        // ---- Диск: последовательная запись и чтение 512 МБ мимо кэша Windows, МБ/с
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteFile(SafeFileHandle h, IntPtr buf, uint n, out uint done, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadFile(SafeFileHandle h, IntPtr buf, uint n, out uint done, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr VirtualAlloc(IntPtr addr, UIntPtr size, uint type, uint protect);
        [DllImport("kernel32.dll")] static extern bool VirtualFree(IntPtr addr, UIntPtr size, uint type);

        static void DiskSpeed(out double writeMBps, out double readMBps)
        {
            const uint Block = 4 << 20; const int Blocks = 128; // 512 МБ
            string path = Path.Combine(Path.GetTempPath(), "tunex-bench.tmp");
            DriveInfo drive = new DriveInfo(Path.GetPathRoot(path));
            if (drive.AvailableFreeSpace < 2L << 30) throw new IOException("мало места");
            IntPtr buf = VirtualAlloc(IntPtr.Zero, (UIntPtr)Block, 0x3000, 0x04);
            if (buf == IntPtr.Zero) throw new OutOfMemoryException();
            try
            {
                byte[] rnd = new byte[Block];
                new Random(42).NextBytes(rnd); // случайные данные — чтобы SSD не «сжимал» нули
                Marshal.Copy(rnd, 0, buf, (int)Block);
                uint done;
                Stopwatch sw = Stopwatch.StartNew();
                using (SafeFileHandle h = CreateFile(path, 0x40000000, 0, IntPtr.Zero, 2, 0x20000000 | 0x80000000, IntPtr.Zero))
                {
                    if (h.IsInvalid) throw new IOException("нет доступа к диску");
                    for (int i = 0; i < Blocks; i++) if (!WriteFile(h, buf, Block, out done, IntPtr.Zero)) throw new IOException("ошибка записи");
                }
                writeMBps = Blocks * (Block / 1048576.0) / sw.Elapsed.TotalSeconds;

                sw.Restart();
                using (SafeFileHandle h = CreateFile(path, 0x80000000, 0, IntPtr.Zero, 3, 0x20000000 | 0x04000000, IntPtr.Zero))
                {
                    if (h.IsInvalid) throw new IOException("нет доступа к диску");
                    for (int i = 0; i < Blocks; i++) if (!ReadFile(h, buf, Block, out done, IntPtr.Zero) || done == 0) break;
                }
                readMBps = Blocks * (Block / 1048576.0) / sw.Elapsed.TotalSeconds;
            }
            finally
            {
                VirtualFree(buf, UIntPtr.Zero, 0x8000);
                try { File.Delete(path); } catch { }
            }
        }

        // Класс видеокарты словами (по таблице моделей)
        public static string GpuTier()
        {
            bool known, integrated;
            int s = GpuDb.Score(out known, out integrated);
            if (SysInfo.Gpus.Count == 0) return "—";
            if (integrated) return "Встроенная";
            if (s < 450) return "Слабая";
            if (s < 750) return "Средняя";
            if (s < 1100) return "Хорошая";
            return "Мощная";
        }

        public static string GpuTierByVram()
        {
            double gb = SysInfo.VramBytes / 1073741824.0;
            bool igpuOnly = SysInfo.Gpus.Count > 0;
            foreach (string g in SysInfo.Gpus)
            {
                bool igpu = (g.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0 && g.IndexOf("Arc", StringComparison.OrdinalIgnoreCase) < 0)
                    || (g.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0 && g.IndexOf(" RX", StringComparison.OrdinalIgnoreCase) < 0 && g.IndexOf("Pro", StringComparison.OrdinalIgnoreCase) < 0);
                if (!igpu) { igpuOnly = false; break; }
            }
            if (igpuOnly || SysInfo.VramBytes < 1L << 30) return "Встроенная";
            if (gb < 4) return "Слабая";
            if (gb < 8) return "Средняя";
            if (gb < 12) return "Хорошая";
            return "Мощная";
        }
    }

    // Оценка видеокарты по названию модели: RTX 4070 (настольная) = 1000.
    // Настоящий тест видеокарты требует DirectX, поэтому берём средние результаты игровых тестов.
    static class GpuDb
    {
        // ключ → очки; порядок важен: сначала более точные названия
        static readonly object[] Table =
        {
            // NVIDIA RTX 50 / 40 / 30 / 20
            "RTX 5090", 2400, "RTX 5080", 1650, "RTX 5070 TI", 1400, "RTX 5070", 1100, "RTX 5060 TI", 820, "RTX 5060", 720, "RTX 5050", 560,
            "RTX 4090", 1900, "RTX 4080 SUPER", 1550, "RTX 4080", 1500, "RTX 4070 TI SUPER", 1250, "RTX 4070 TI", 1150, "RTX 4070 SUPER", 1150,
            "RTX 4070", 1000, "RTX 4060 TI", 780, "RTX 4060", 640, "RTX 4050", 520,
            "RTX 3090 TI", 1250, "RTX 3090", 1150, "RTX 3080 TI", 1120, "RTX 3080", 1050, "RTX 3070 TI", 880, "RTX 3070", 830,
            "RTX 3060 TI", 730, "RTX 3060", 570, "RTX 3050 TI", 380, "RTX 3050", 400,
            "RTX 2080 TI", 850, "RTX 2080 SUPER", 730, "RTX 2080", 700, "RTX 2070 SUPER", 650, "RTX 2070", 580, "RTX 2060 SUPER", 540, "RTX 2060", 480,
            "RTX A2000", 450, "RTX A4000", 750, "RTX A5000", 950,
            // NVIDIA GTX / MX
            "GTX 1660 TI", 430, "GTX 1660 SUPER", 420, "GTX 1660", 380, "GTX 1650 SUPER", 320, "GTX 1650 TI", 270, "GTX 1650", 250, "GTX 1630", 170,
            "GTX 1080 TI", 640, "GTX 1080", 500, "GTX 1070 TI", 460, "GTX 1070", 420, "GTX 1060", 300, "GTX 1050 TI", 170, "GTX 1050", 140,
            "GTX 980 TI", 380, "GTX 980", 320, "GTX 970", 270, "GTX 960", 180, "GTX 950", 140,
            "MX 570", 160, "MX 550", 140, "MX 450", 120, "MX 350", 80, "MX 330", 80, "MX 250", 70, "MX 230", 60, "MX 150", 60, "MX 130", 45, "MX 110", 35,
            // AMD RX
            "RX 9070 XT", 1350, "RX 9070", 1200, "RX 9060 XT", 800, "RX 9060", 700,
            "RX 7900 XTX", 1600, "RX 7900 XT", 1350, "RX 7900 GRE", 1150, "RX 7800 XT", 1100, "RX 7700 XT", 950, "RX 7700", 900, "RX 7600 XT", 680, "RX 7600", 640,
            "RX 6950 XT", 1250, "RX 6900 XT", 1200, "RX 6800 XT", 1130, "RX 6800", 1000, "RX 6750 XT", 850, "RX 6700 XT", 800, "RX 6700", 720,
            "RX 6650 XT", 650, "RX 6600 XT", 620, "RX 6600", 560, "RX 6550", 330, "RX 6500 XT", 300, "RX 6500", 280, "RX 6400", 230,
            "RX 5700 XT", 580, "RX 5700", 520, "RX 5600 XT", 470, "RX 5600", 440, "RX 5500 XT", 330, "RX 5500", 300, "RX 5300", 250,
            "RX 590", 300, "RX 580", 280, "RX 570", 240, "RX 560", 150, "RX 550", 100, "RX 480", 270, "RX 470", 230,
            "VEGA 64", 450, "VEGA 56", 400,
            // Intel Arc (отдельные)
            "ARC B580", 700, "ARC B570", 620, "ARC A770", 620, "ARC A750", 580, "ARC A580", 480, "ARC A380", 230, "ARC A310", 170,
        };

        // Встроенная графика: значение без поправки на ноутбук
        static readonly object[] Integrated =
        {
            "RADEON 890M", 300, "RADEON 880M", 270, "RADEON 8060S", 900, "RADEON 8050S", 750, "RADEON 780M", 230, "RADEON 760M", 190, "RADEON 740M", 120,
            "RADEON 680M", 200, "RADEON 660M", 140, "RADEON 610M", 50,
            "ARC 140V", 200, "ARC 130V", 170, "ARC 140T", 190, "ARC 130T", 160, "ARC GRAPHICS", 170,
            "IRIS XE", 90, "IRIS PLUS", 60, "IRIS", 50, "UHD GRAPHICS", 40, "HD GRAPHICS", 30,
            "RADEON VEGA", 80, "VEGA 8", 80, "VEGA 11", 100, "RADEON GRAPHICS", 90, "RADEON R7", 40, "RADEON R5", 30,
        };

        static Regex Pattern(string key)
        {
            string[] parts = key.Split(' ');
            for (int i = 0; i < parts.Length; i++) parts[i] = Regex.Escape(parts[i]);
            // «RTX 4070» совпадает с «RTX4070», «RTX 4070 Laptop GPU», но не с «RTX 4070 Ti»; суффикс M/S — мобильная версия
            return new Regex("(?<![0-9A-Z])" + string.Join("\\s*", parts) + "(M|S)?(?![0-9A-Z])");
        }

        static string Normalize(string name)
        {
            string n = name.ToUpperInvariant().Replace("(TM)", " ").Replace("(R)", " ").Replace("™", " ").Replace("®", " ");
            return Regex.Replace(n, "\\s+", " ").Trim();
        }

        // Оценка одной видеокарты; -1 — неизвестная
        public static int ScoreOf(string name, out bool integrated)
        {
            integrated = false;
            string n = Normalize(name);
            for (int i = 0; i < Table.Length; i += 2)
            {
                Match m = Pattern((string)Table[i]).Match(n);
                if (!m.Success) continue;
                bool laptop = m.Groups[1].Success || n.Contains("LAPTOP") || n.Contains("MOBILE") || n.Contains("MAX-Q");
                return (int)Math.Round((int)Table[i + 1] * (laptop ? 0.75 : 1.0));
            }
            for (int i = 0; i < Integrated.Length; i += 2)
                if (n.Contains((string)Integrated[i])) { integrated = true; return (int)Integrated[i + 1]; }
            if (n.Contains("MICROSOFT BASIC") || n.Contains("REMOTE") || n.Contains("VIRTUAL") || n.Contains("PARSEC")) { integrated = true; return 0; }
            return -1;
        }

        // Лучшая видеокарта в системе (в ноутбуке со встроенной и отдельной — отдельная)
        public static int Score(out bool known, out bool integrated)
        {
            known = false; integrated = false;
            int best = -1; bool bestIntegrated = false;
            foreach (string g in SysInfo.Gpus)
            {
                bool igpu;
                int s = ScoreOf(g, out igpu);
                if (s < 0) continue;
                if (s > best) { best = s; bestIntegrated = igpu; }
            }
            if (best >= 0) { known = true; integrated = bestIntegrated; return best; }
            // Неизвестная модель: грубо по видеопамяти
            if (SysInfo.Gpus.Count == 0) return 0;
            double gb = SysInfo.VramBytes / 1073741824.0;
            if (gb < 1) { integrated = true; return 60; }
            if (gb < 4) return 200;
            if (gb < 6) return 300;
            if (gb < 8) return 450;
            if (gb < 12) return 600;
            if (gb < 16) return 800;
            return 1000;
        }
    }
}
