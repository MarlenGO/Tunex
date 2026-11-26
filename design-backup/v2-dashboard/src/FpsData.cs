using System;
using System.Collections.Generic;

namespace Tunex
{
    // Популярная игра и типичный FPS в Full HD на опорной системе: RTX 3060 (оценка 570) + Core i5-12400 (оценка ≈ 140).
    // Числа — примерные средние значения из обзоров, собранные вручную; правьте их здесь.
    class PopularGame
    {
        public string Name, Genre;
        public int AppId;          // 0 — игры нет в Steam
        public int Low, High;      // FPS на низких и высоких настройках, 1080p, опорная система
        public int CpuCap;         // сколько FPS выдаёт опорный процессор, если видеокарта не мешает
        public int EngineCap;      // предел самой игры (0 — нет)
        public string Note;

        public PopularGame(string name, string genre, int appid, int low, int high, int cpuCap, int engineCap, string note)
        {
            Name = name; Genre = genre; AppId = appid; Low = low; High = high; CpuCap = cpuCap; EngineCap = engineCap; Note = note;
        }
    }

    enum Preset { Low1080, High1080, High1440 }

    static class FpsData
    {
        public const int RefGpu = 570, RefCpu = 140;

        public static readonly PopularGame[] Games =
        {
            new PopularGame("Counter-Strike 2", "Шутер", 730, 330, 240, 380, 0, null),
            new PopularGame("Dota 2", "MOBA", 570, 220, 160, 200, 0, null),
            new PopularGame("Valorant", "Шутер", 0, 450, 330, 420, 0, "Нет в Steam — требования не сравниваются."),
            new PopularGame("Fortnite", "Королевская битва", 0, 260, 105, 260, 0, "Нет в Steam. «Низкие» — режим «Производительность»."),
            new PopularGame("PUBG: Battlegrounds", "Королевская битва", 578080, 190, 125, 180, 0, null),
            new PopularGame("Apex Legends", "Королевская битва", 1172470, 200, 140, 240, 300, null),
            new PopularGame("Rust", "Выживание", 252490, 110, 70, 100, 0, "Сильно зависит от сервера и числа построек вокруг."),
            new PopularGame("Grand Theft Auto V", "Экшен", 271590, 180, 110, 170, 188, null),
            new PopularGame("Call of Duty: Warzone", "Королевская битва", 1962663, 140, 95, 170, 0, null),
            new PopularGame("Marvel Rivals", "Шутер", 2767030, 120, 70, 170, 0, null),
            new PopularGame("Overwatch 2", "Шутер", 2357570, 290, 170, 400, 600, null),
            new PopularGame("Rainbow Six Siege", "Шутер", 359550, 330, 220, 400, 0, null),
            new PopularGame("Minecraft (Java)", "Песочница", 0, 300, 200, 230, 0, "Нет в Steam. Без модов и шейдеров, дальность 12 чанков."),
            new PopularGame("League of Legends", "MOBA", 0, 450, 320, 380, 0, "Нет в Steam — требования не сравниваются."),
            new PopularGame("Cyberpunk 2077", "RPG", 1091500, 100, 65, 140, 0, "Без трассировки лучей и генерации кадров."),
            new PopularGame("Red Dead Redemption 2", "Экшен", 1174180, 95, 60, 140, 0, null),
            new PopularGame("Elden Ring", "RPG", 1245620, 110, 75, 100, 60, "Игра ограничивает FPS: не больше 60."),
            new PopularGame("Baldur's Gate 3", "RPG", 1086940, 110, 75, 95, 0, "В третьем акте FPS ниже: там упирается в процессор."),
            new PopularGame("Helldivers 2", "Шутер", 553850, 95, 60, 90, 0, null),
            new PopularGame("Forza Horizon 5", "Гонки", 1551360, 160, 105, 190, 0, null),
        };

        // Видеокарты для таблицы FPS одной игры по сериям: первая строка — название группы.
        // Оценки берутся из таблицы GpuDb (src/Bench.cs), внутри группы строки сортируются по силе.
        public static readonly string[][] GpuGroups =
        {
            new string[] { "Старые серии · GTX 10 и 16", "GeForce GTX 1050 Ti", "GeForce GTX 1060", "GeForce GTX 1070", "GeForce GTX 1080", "GeForce GTX 1080 Ti",
                "GeForce GTX 1650", "GeForce GTX 1660 Super", "GeForce GTX 1660 Ti" },
            new string[] { "RTX 20 серия", "GeForce RTX 2060", "GeForce RTX 2060 Super", "GeForce RTX 2070", "GeForce RTX 2070 Super", "GeForce RTX 2080",
                "GeForce RTX 2080 Super", "GeForce RTX 2080 Ti" },
            new string[] { "RTX 30 серия", "GeForce RTX 3050", "GeForce RTX 3060", "GeForce RTX 3060 Ti", "GeForce RTX 3070", "GeForce RTX 3070 Ti",
                "GeForce RTX 3080", "GeForce RTX 3080 Ti", "GeForce RTX 3090" },
            new string[] { "RTX 40 серия", "GeForce RTX 4060", "GeForce RTX 4060 Ti", "GeForce RTX 4070", "GeForce RTX 4070 Super", "GeForce RTX 4070 Ti",
                "GeForce RTX 4070 Ti Super", "GeForce RTX 4080", "GeForce RTX 4080 Super", "GeForce RTX 4090" },
            new string[] { "RTX 50 серия", "GeForce RTX 5050", "GeForce RTX 5060", "GeForce RTX 5060 Ti", "GeForce RTX 5070", "GeForce RTX 5070 Ti",
                "GeForce RTX 5080", "GeForce RTX 5090" },
            new string[] { "Другие видеокарты · AMD Radeon и Intel Arc", "Radeon RX 5500 XT", "Radeon RX 5600 XT", "Radeon RX 5700 XT", "Radeon RX 6500 XT", "Radeon RX 6600",
                "Radeon RX 6650 XT", "Radeon RX 6700 XT", "Radeon RX 6750 XT", "Radeon RX 6800", "Radeon RX 6800 XT", "Radeon RX 6900 XT",
                "Radeon RX 7600", "Radeon RX 7600 XT", "Radeon RX 7700 XT", "Radeon RX 7800 XT", "Radeon RX 7900 GRE",
                "Radeon RX 7900 XT", "Radeon RX 7900 XTX", "Radeon RX 9060 XT", "Radeon RX 9070", "Radeon RX 9070 XT",
                "Intel Arc A580", "Intel Arc A750", "Intel Arc A770", "Intel Arc B570", "Intel Arc B580" },
        };

        // Все видеокарты таблицы одним списком (для проверок)
        public static string[] TableGpus
        {
            get
            {
                List<string> all = new List<string>();
                foreach (string[] g in GpuGroups) for (int i = 1; i < g.Length; i++) all.Add(g[i]);
                return all.ToArray();
            }
        }

        // Процессоры на выбор в таблице FPS: название для оценки → подпись
        public static readonly string[][] TableCpus =
        {
            new string[] { "Intel Core i3-10100", "Слабый: Core i3-10100" },
            new string[] { "AMD Ryzen 5 5600", "Средний: Ryzen 5 5600" },
            new string[] { "Intel Core i5-13600K", "Сильный: Core i5-13600K" },
            new string[] { "AMD Ryzen 7 7800X3D", "Топ: Ryzen 7 7800X3D" },
        };

        // Примерный FPS. gpu/cpu — оценки Tunex; cpu < 0 — процессор неизвестен, считаем только видеокарту.
        // cpuBound = true, если упирается в процессор (видеокарта могла бы больше).
        public static int Estimate(PopularGame g, Preset p, int gpu, int cpu, out bool cpuBound, out bool engineBound)
        {
            cpuBound = false; engineBound = false;
            if (gpu <= 0) return -1;
            double baseFps = p == Preset.Low1080 ? g.Low : g.High;
            double byGpu = baseFps * gpu / RefGpu;
            if (p == Preset.High1440) byGpu *= 0.7;
            double fps = byGpu;
            if (cpu > 0)
            {
                double byCpu = g.CpuCap * cpu / (double)RefCpu;
                if (byCpu < fps) { fps = byCpu; cpuBound = true; }
            }
            if (g.EngineCap > 0 && fps > g.EngineCap) { fps = g.EngineCap; engineBound = true; cpuBound = false; }
            return Round(fps);
        }

        static int Round(double f)
        {
            if (f < 50) return (int)Math.Round(f);
            return (int)(Math.Round(f / 5.0) * 5);
        }

        public static int GpuScore(string name) { bool ig; return GpuDb.ScoreOf(name, out ig); }
        public static int CpuScore(string name) { string m; return CpuDb.ScoreOf(name, 0, out m); }
    }
}
