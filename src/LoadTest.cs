using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;

namespace Tunex
{
    // ---------------------------------------------------------------- Пинг под нагрузкой: растёт ли задержка, когда канал занят
    // Так проверяют «раздувание буфера» (bufferbloat): роутер копит очередь пакетов, и пинг в игре растёт,
    // пока кто-то дома смотрит видео или качает. Проверочные данные — сервис скорости Cloudflare, в память, без записи на диск.
    class LoadPhase
    {
        public string Name;
        public readonly List<int> Rtt = new List<int>();
        public int Sent, Lost;
        public float Mbps = -1;
        public float Median { get { if (Rtt.Count == 0) return -1; List<int> l = new List<int>(Rtt); l.Sort(); return l[l.Count / 2]; } }
        public float Loss { get { return Sent > 0 ? Lost * 100f / Sent : 0; } }
    }

    class LoadResult
    {
        public LoadPhase Idle, Down, Up;
        public string Error;
        public float Worst { get { float w = 0; foreach (LoadPhase p in new[] { Down, Up }) if (p != null && p.Mbps > 0 && p.Median >= 0 && Idle.Median >= 0) w = Math.Max(w, p.Median - Idle.Median); return w; } }
    }

    static class LoadTest
    {
        public const string Target = "1.1.1.1";              // рядом почти у всех: Cloudflare стоит во многих городах
        const string DownUrl = "https://speed.cloudflare.com/__down?bytes=90000000";   // больше 100 МБ за раз сервис не отдаёт
        const string UpUrl = "https://speed.cloudflare.com/__up";
        public const int Seconds = 8;                         // на скачивание и на отправку
        const int Streams = 4;

        public static LoadResult Run(Action<string> progress)
        {
            LoadResult r = new LoadResult();
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }   // TLS 1.2
            if (ServicePointManager.DefaultConnectionLimit < 16) ServicePointManager.DefaultConnectionLimit = 16;
            IPAddress ip = IPAddress.Parse(Target);

            progress("Пинг без нагрузки…");
            r.Idle = new LoadPhase(); r.Idle.Name = "Без нагрузки";
            PingFor(ip, r.Idle, 3000, null);
            if (r.Idle.Rtt.Count == 0) { r.Error = "Нет ответа от " + Target + ". Проверьте, что интернет подключён."; return r; }

            progress("Скачиваю проверочные данные и меряю пинг…");
            r.Down = Load(ip, true);
            progress("Отправляю проверочные данные и меряю пинг…");
            r.Up = Load(ip, false);
            if (r.Down.Mbps <= 0 && r.Up.Mbps <= 0) r.Error = "Не удалось загрузить канал: сервис проверки скорости не ответил. Попробуйте позже.";
            return r;
        }

        static LoadPhase Load(IPAddress ip, bool down)
        {
            LoadPhase ph = new LoadPhase(); ph.Name = down ? "При скачивании" : "При отправке";
            long bytes = 0, counted = 0;
            bool stop = false;
            Stopwatch clock = Stopwatch.StartNew();
            List<Thread> workers = new List<Thread>();
            List<WebRequest> reqs = new List<WebRequest>();
            for (int i = 0; i < Streams; i++)
            {
                Thread t = new Thread(delegate()
                {
                    byte[] buf = new byte[64 * 1024];
                    new Random().NextBytes(buf);
                    while (!stop)
                    {
                        try
                        {
                            HttpWebRequest w = (HttpWebRequest)WebRequest.Create(down ? DownUrl : UpUrl);
                            w.UserAgent = "Tunex/" + Theme.Version;
                            w.Timeout = 10000; w.ReadWriteTimeout = 10000;
                            lock (reqs) reqs.Add(w);
                            if (down)
                            {
                                using (WebResponse resp = w.GetResponse())
                                using (Stream s = resp.GetResponseStream())
                                {
                                    int n;
                                    while (!stop && (n = s.Read(buf, 0, buf.Length)) > 0) Interlocked.Add(ref bytes, n);
                                }
                            }
                            else
                            {
                                w.Method = "POST"; w.ContentType = "application/octet-stream";
                                w.ContentLength = 50L << 20; w.AllowWriteStreamBuffering = false;
                                using (Stream s = w.GetRequestStream())
                                {
                                    long left = w.ContentLength;
                                    while (!stop && left > 0)
                                    {
                                        int n = (int)Math.Min(buf.Length, left);
                                        s.Write(buf, 0, n); left -= n; Interlocked.Add(ref bytes, n);
                                    }
                                }
                                if (!stop) using (WebResponse resp = w.GetResponse()) { }
                            }
                        }
                        catch { if (!stop) Thread.Sleep(300); }
                    }
                });
                t.IsBackground = true; t.Start(); workers.Add(t);
            }
            // Первая секунда — разгон, её не считаем ни в скорость, ни в пинг
            Thread.Sleep(1200);
            long b0 = Interlocked.Read(ref bytes); double t0 = clock.Elapsed.TotalSeconds;
            PingFor(ip, ph, Seconds * 1000 - 1200, null);
            counted = Interlocked.Read(ref bytes) - b0;
            double secs = clock.Elapsed.TotalSeconds - t0;
            stop = true;
            lock (reqs) foreach (WebRequest w in reqs) { try { w.Abort(); } catch { } }
            foreach (Thread t in workers) t.Join(3000);
            if (counted > 0 && secs > 0.5) ph.Mbps = (float)(counted * 8 / secs / 1e6);
            return ph;
        }

        static void PingFor(IPAddress ip, LoadPhase ph, int ms, Func<bool> cancel)
        {
            Stopwatch all = Stopwatch.StartNew();
            using (Ping p = new Ping())
                while (all.ElapsedMilliseconds < ms)
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    ph.Sent++;
                    try
                    {
                        PingReply r = p.Send(ip, 1500);
                        if (r.Status == IPStatus.Success) ph.Rtt.Add((int)r.RoundtripTime); else ph.Lost++;
                    }
                    catch { ph.Lost++; }
                    int wait = 150 - (int)sw.ElapsedMilliseconds;
                    if (wait > 0) Thread.Sleep(wait);
                }
        }

        // Итог простыми словами
        public static string Verdict(LoadResult r, out string text, out System.Drawing.Color c)
        {
            if (r.Error != null) { text = r.Error; c = Theme.Orange; return "Проверка не удалась"; }
            float idle = r.Idle.Median, grow = r.Worst;
            float loss = Math.Max(r.Down != null && r.Down.Mbps > 0 ? r.Down.Loss : 0, r.Up != null && r.Up.Mbps > 0 ? r.Up.Loss : 0);
            string part = r.Down == null || r.Down.Mbps <= 0 ? " Скачивание проверить не удалось — оценка только по отправке." : r.Up == null || r.Up.Mbps <= 0 ? " Отправку проверить не удалось — оценка только по скачиванию." : "";
            string tail = "Пинг до ближайшего сервера Cloudflare: в покое " + NetPing.Ms(idle) + ", под нагрузкой до " + NetPing.Ms(idle + grow) + "." + part;
            if (grow < 30 && loss < 8)
            {
                c = Theme.Green; text = "Даже когда канал занят полностью, пинг почти не растёт. Можно спокойно качать и смотреть видео рядом с игрой. " + tail;
                return "Роутер справляется с нагрузкой";
            }
            string fix = " Что сделать: в настройках роутера включите управление очередью — оно называется SQM, Smart Queue, QoS или «Приоритизация трафика» " +
                "(у Keenetic — IntelliQoS, у ASUS — Adaptive QoS, у TP-Link — QoS) — и укажите скорость на 10–15% ниже вашего тарифа. Если такого нет — ставьте загрузки на паузу во время игры.";
            if (grow < 80 && loss < 15)
            {
                c = Theme.Orange; text = "Когда кто-то дома качает или смотрит видео, пинг в игре заметно растёт. " + tail + fix;
                return "Пинг растёт под нагрузкой";
            }
            c = Theme.Red; text = "Когда канал занят, роутер копит очередь пакетов, и игра начинает лагать: " + tail +
                (loss >= 8 ? " Ещё и теряются пакеты — " + NetPing.Pct(loss) + "." : "") + fix;
            return "Сильные лаги, когда интернет занят";
        }

        public static string Speed(float mbps)
        {
            if (mbps < 0) return "—";
            return (mbps >= 100 ? Math.Round(mbps).ToString(CultureInfo.InvariantCulture) : mbps.ToString("0.#", CultureInfo.GetCultureInfo("ru-RU"))) + " Мбит/с";
        }
    }
}
