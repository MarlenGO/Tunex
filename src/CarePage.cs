using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace Tunex
{
    // ---------------------------------------------------------------- Раздел «Обслуживание»: мышь, место на диске, автозагрузка
    class CarePage : Page
    {
        readonly Card mouseCard, cleanCard, bootCard, startCard;
        readonly MousePad pad;
        int mouseHz = -1, mouseSteady = -1;
        List<CleanItem> clean; bool cleanBusy, cleaning;
        List<StartupItem> starts; List<BootInfo> boots; bool startBusy;

        public CarePage()
        {
            Add(new PageHeader(Icons.Repair, "Обслуживание", "Мышь, место на диске и автозагрузка — то, что незаметно мешает играть.", null));

            Add(new SectionLabel("Мышь"));
            mouseCard = Add(new Card());
            pad = Add(new MousePad());
            pad.Visible = false;
            pad.Done += delegate(int hz, int steady) { mouseHz = hz; mouseSteady = steady; pad.Visible = false; FillMouse(); PerformLayout(); };

            Add(new SectionLabel("Место на диске"));
            cleanCard = Add(new Card());

            Add(new SectionLabel("Автозагрузка"));
            bootCard = Add(new Card());
            startCard = Add(new Card());

            FillMouse(); FillClean(); FillStart();
        }

        public override void OnShow()
        {
            if (clean == null && !cleanBusy) ScanClean();
            if (starts == null && !startBusy) ScanStart();
            // Для проверки: --mouse сразу запускает проверку мыши
            if (!autoMouse && Array.IndexOf(Environment.GetCommandLineArgs(), "--mouse") >= 0) { autoMouse = true; mouseCard.Visible = false; pad.Visible = true; pad.Start(); PerformLayout(); }
        }
        bool autoMouse;

        void Ui(Action a) { try { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); } catch { } }

        // ================================================================ Мышь
        void FillMouse()
        {
            mouseCard.SuspendLayout(); mouseCard.Clear();
            FlatButton go = new FlatButton(mouseHz > 0 ? "Проверить ещё раз" : "Проверить мышь", false, Icons.Play);
            go.Click += delegate { mouseCard.Visible = false; pad.Visible = true; pad.Start(); PerformLayout(); };
            if (mouseHz <= 0)
            {
                SettingRow r = mouseCard.Add(new SettingRow(mouseHz == 0 ? "Не удалось поймать движение — попробуйте ещё раз" : "Какая частота у вашей мыши?",
                    "Частота опроса — сколько раз в секунду мышь сообщает о движении. Многие игровые мыши из коробки работают на 125 Гц вместо 1000. Проверка займёт 5 секунд: нужно просто водить мышью.", go));
                r.Glyph = Icons.Info; r.GlyphColor = mouseHz == 0 ? Theme.Orange : Theme.Accent;
            }
            else
            {
                string text; Color c;
                string title = MouseRate.Verdict(mouseHz, out text, out c);
                SettingRow r = mouseCard.Add(new SettingRow(title, text, go));
                r.Glyph = c == Theme.Green ? Icons.Check : Icons.Warning; r.GlyphColor = c;
                r.Badges.Add(new Badge("Измерено: " + mouseHz + " Гц", Theme.Text2));
                if (mouseSteady >= 0 && mouseSteady < 80) r.Badges.Add(new Badge("Неровно: " + mouseSteady + "%", Theme.Orange));
                mouseCard.Footer = mouseSteady >= 0 && mouseSteady < 80
                    ? "Частота часто проседала. Так бывает у беспроводных мышей при слабом сигнале (приёмник далеко или за корпусом ПК) и при разряженной батарее. Подключите приёмник в передний порт или через удлинитель."
                    : null;
            }
            mouseCard.Visible = true;
            mouseCard.ResumeLayout(); mouseCard.Relayout();
        }

        // ================================================================ Место на диске
        void ScanClean()
        {
            cleanBusy = true; FillClean();
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<CleanItem> l = new List<CleanItem>();
                try { l = Cleaner.Items(); foreach (CleanItem i in l) Cleaner.Measure(i); } catch { }
                Ui(delegate { clean = l; cleanBusy = false; FillClean(); });
            });
        }

        void FillClean()
        {
            SuspendLayout();
            cleanCard.SuspendLayout(); cleanCard.Clear();
            if (clean == null || cleanBusy)
            {
                SettingRow w = cleanCard.Add(new SettingRow(cleaning ? "Удаляю…" : "Считаю, сколько места можно освободить…", "Несколько секунд.", null));
                w.Glyph = Icons.Refresh; w.GlyphColor = Theme.Text3;
            }
            else
            {
                FlatButton go = new FlatButton("Очистить выбранное", true, Icons.Broom);
                go.Click += delegate { CleanNow(); };
                SettingRow sum = cleanCard.Add(new SettingRow("Можно освободить " + Unload.Size(PickedSize()), "Выбрано то, что безопасно удалить. Отметки Tunex запомнит.", go));
                sum.Glyph = Icons.Broom; sum.GlyphColor = Theme.Accent;
                foreach (CleanItem i in clean)
                {
                    CleanItem ii = i;
                    ToggleSwitch sw = new ToggleSwitch();
                    sw.Checked = Cleaner.Picked(i) && i.Size > 0 && i.Blocked == null;
                    sw.Enabled = i.Blocked == null && i.Size > 0;
                    SettingRow r = new SettingRow(i.Title, (i.Blocked != null ? i.Blocked + " " : "") + i.Desc, sw);
                    sw.CheckedChanged += delegate { Cleaner.SetPicked(ii, sw.Checked); sum.Title = "Можно освободить " + Unload.Size(PickedSize()); sum.Invalidate(); };
                    r.Glyph = i.Key == "steam" ? Icons.Library : i.Key == "shaders" ? Icons.Gpu : Icons.Save; r.GlyphColor = Theme.Text2;
                    r.Badges.Add(new Badge(i.Size > 0 ? Unload.Size(i.Size) : "пусто", i.Size >= (1L << 30) ? Theme.Accent : Theme.Text2));
                    cleanCard.Add(r);
                }
                cleanCard.Footer = "Tunex удаляет только то, что Windows и программы создадут заново. Игры, сохранения и личные файлы не трогаются. Занятые файлы пропускаются." +
                    (SysInfo.IsAdmin ? "" : " С правами администратора Tunex найдёт ещё скачанные обновления Windows и системные временные файлы.");
            }
            cleanCard.ResumeLayout(); cleanCard.Relayout();
            ResumeLayout();
        }

        long PickedSize()
        {
            long n = 0;
            if (clean != null) foreach (CleanItem i in clean) if (i.Blocked == null && Cleaner.Picked(i)) n += i.Size;
            return n;
        }

        void CleanNow()
        {
            if (cleaning || clean == null) return;
            List<CleanItem> pick = new List<CleanItem>();
            foreach (CleanItem i in clean) if (i.Blocked == null && i.Size > 0 && Cleaner.Picked(i)) pick.Add(i);
            if (pick.Count == 0) { App.Toast("Нечего чистить — включите переключатели у того, что удалить.", Theme.Orange); return; }
            cleaning = true; cleanBusy = true; FillClean();
            ThreadPool.QueueUserWorkItem(delegate
            {
                long freed = 0;
                foreach (CleanItem i in pick) { try { freed += Cleaner.Clean(i); } catch { } }
                Ui(delegate
                {
                    cleaning = false; cleanBusy = false; FillClean();
                    App.Toast("Освобождено " + Unload.Size(freed) + ".", Theme.Green);
                });
            });
        }

        // ================================================================ Автозагрузка
        void ScanStart()
        {
            startBusy = true; FillStart();
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<StartupItem> l = new List<StartupItem>(); List<BootInfo> b = new List<BootInfo>();
                try { l = Startup.List(); Startup.AttachSlow(l); b = Startup.Boots(10); } catch { }
                Ui(delegate { starts = l; boots = b; startBusy = false; FillStart(); });
            });
        }

        void FillStart()
        {
            SuspendLayout();
            bootCard.SuspendLayout(); bootCard.Clear();
            startCard.SuspendLayout(); startCard.Clear();
            if (starts == null || startBusy)
            {
                SettingRow w = bootCard.Add(new SettingRow("Смотрю, что запускается вместе с Windows…", "Несколько секунд.", null));
                w.Glyph = Icons.Refresh; w.GlyphColor = Theme.Text3;
                startCard.Visible = false;
            }
            else
            {
                BootRow();
                List<StartupItem> l = new List<StartupItem>(starts);
                l.Sort(delegate(StartupItem a, StartupItem b) { int c = b.SlowSec.CompareTo(a.SlowSec); return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); });
                int on = 0; foreach (StartupItem i in l) if (i.Enabled) on++;
                foreach (StartupItem i in l)
                {
                    StartupItem ii = i;
                    ToggleSwitch sw = new ToggleSwitch();
                    sw.Checked = i.Enabled;
                    sw.Enabled = !i.NeedsAdmin || SysInfo.IsAdmin;
                    string desc = "Запускается " + i.Source + ". " + (i.SlowSec >= 0.5f
                        ? "Windows замечала, что эта программа замедляет загрузку, — в среднем на " + Sec(i.SlowSec) + " (раз за 2 месяца: " + i.SlowCount + ")."
                        : "");
                    SettingRow r = new SettingRow(i.Name, desc.Trim(), sw);
                    sw.CheckedChanged += delegate
                    {
                        if (sw.Checked == ii.Enabled) return;
                        string err = Startup.SetEnabled(ii, sw.Checked);
                        if (err != null) { App.Error(err); sw.Checked = ii.Enabled; return; }
                        App.Toast((sw.Checked ? "Будет запускаться с Windows: " : "Больше не запускается с Windows: ") + ii.Name + ".", Theme.Green);
                    };
                    r.Glyph = Icons.Play; r.GlyphColor = Theme.Text2;
                    if (i.SlowSec >= 2) r.Badges.Add(new Badge("Замедляет загрузку", Theme.Orange));
                    if (i.NeedsAdmin && !SysInfo.IsAdmin) r.Badges.Add(new Badge("Нужны права администратора", Theme.Text2));
                    startCard.Add(r);
                }
                if (l.Count == 0)
                {
                    SettingRow e = startCard.Add(new SettingRow("Вместе с Windows ничего не запускается", "Отлично — загрузка ничем не нагружена.", null));
                    e.Glyph = Icons.Check; e.GlyphColor = Theme.Green;
                }
                startCard.Footer = "Включено: " + on + " из " + l.Count + ". Выключенная программа просто не запускается сама — открыть её можно как обычно. " +
                    "Это то же самое, что вкладка «Автозагрузка» в Диспетчере задач. Службы и драйверы здесь не показаны.";
                startCard.Visible = true;
            }
            bootCard.ResumeLayout(); bootCard.Relayout();
            startCard.ResumeLayout(); startCard.Relayout();
            ResumeLayout();
            PerformLayout();
        }

        void BootRow()
        {
            FlatButton again = new FlatButton("Обновить", false, Icons.Refresh);
            again.Click += delegate { ScanStart(); };
            if (boots == null || boots.Count == 0)
            {
                SettingRow r = bootCard.Add(new SettingRow("Время загрузки Windows неизвестно",
                    Startup.BootError ?? "Windows ещё не записала время загрузки. Оно появится после следующего включения компьютера.", again));
                r.Glyph = Icons.Info; r.GlyphColor = Theme.Text2;
                return;
            }
            BootInfo last = boots[0];
            float avg = 0; foreach (BootInfo b in boots) avg += b.Seconds; avg /= boots.Count;
            Color c = last.Seconds <= 40 ? Theme.Green : last.Seconds <= 90 ? Theme.Orange : Theme.Red;
            string text = "До рабочего стола — " + Sec(last.MainSeconds) + ", ещё " + Sec(Math.Max(0, last.Seconds - last.MainSeconds)) + " Windows догружала программы автозагрузки. " +
                (boots.Count > 1 ? "В среднем за последние " + boots.Count + " включений — " + Sec(avg) + ". " : "") +
                (last.Seconds > 40 ? "Чем меньше программ в автозагрузке, тем быстрее компьютер готов к игре." : "Это быстро.");
            SettingRow row = bootCard.Add(new SettingRow("Последняя загрузка Windows: " + Sec(last.Seconds), text, again));
            row.Glyph = c == Theme.Green ? Icons.Check : Icons.Warning; row.GlyphColor = c;
            row.Badges.Add(new Badge(Backups.Short(last.When), Theme.Text2));
        }

        static string Sec(float s) { return (s >= 10 ? Math.Round(s).ToString(CultureInfo.InvariantCulture) : s.ToString("0.#", CultureInfo.GetCultureInfo("ru-RU"))) + " с"; }
    }

    // ---------------------------------------------------------------- Поле для проверки мыши
    class MousePad : BufferedControl
    {
        public event Action<int, int> Done;
        readonly MouseRate rate = new MouseRate();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly Stopwatch clock = new Stopwatch();
        long last, active;   // тики: последнее движение и сколько всего мышь двигалась
        bool running;
        const double Need = 4.0;   // секунд движения

        public MousePad()
        {
            Height = Theme.S(200);
            timer.Interval = 100;
            timer.Tick += delegate
            {
                Invalidate();
                if (!running) return;
                if (Moved >= Need || clock.Elapsed.TotalSeconds > 20) Finish();
            };
            Disposed += delegate { Stop(); timer.Dispose(); };
        }

        double Moved { get { return active / (double)Stopwatch.Frequency; } }

        public void Start()
        {
            rate.Clear(); active = 0; last = 0;
            clock.Restart();
            if (!MouseRate.Register(Handle, true)) { running = false; if (Done != null) Done(0, -1); return; }
            running = true; timer.Start(); Invalidate();
        }

        void Stop() { if (running) { running = false; try { MouseRate.Register(Handle, false); } catch { } } timer.Stop(); }

        void Finish()
        {
            Stop();
            int hz = rate.Measured();
            if (Done != null) Done(hz > 0 ? hz : 0, rate.Steady());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == MouseRate.WM_INPUT && running)
            {
                long now = clock.ElapsedTicks;
                if (MouseRate.IsMove(m.LParam))
                {
                    if (last > 0 && now - last < Stopwatch.Frequency / 20) active += now - last;
                    last = now;
                    rate.Add(now);
                }
            }
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            RectangleF card = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.PaintCard(g, card, Theme.Sf(14));
            float p = (float)Math.Min(1, Moved / Need);
            int cx = Width / 2;
            Theme.DrawText(g, "Водите мышью быстрыми кругами", Theme.FontCellTitle, Theme.Text, new Rectangle(0, Theme.S(34), Width, Theme.S(26)), TextFormatFlags.HorizontalCenter);
            int live = rate.Measured();
            Theme.DrawText(g, live > 0 ? "Сейчас: около " + live + " Гц" : "Можно в любом месте экрана — Tunex считает сигналы самой мыши", Theme.FontSmall, Theme.Text2,
                new Rectangle(0, Theme.S(66), Width, Theme.S(20)), TextFormatFlags.HorizontalCenter);
            Rectangle bar = new Rectangle(cx - Theme.S(160), Theme.S(110), Theme.S(320), Theme.S(8));
            Theme.FillRound(g, Theme.Field, bar, bar.Height / 2f);
            if (p > 0) Theme.FillRound(g, Theme.Accent, new RectangleF(bar.X, bar.Y, Math.Max(bar.Height, bar.Width * p), bar.Height), bar.Height / 2f);
            Theme.DrawText(g, Math.Round(p * 100) + "%", Theme.FontSmall, Theme.Text3, new Rectangle(0, Theme.S(128), Width, Theme.S(20)), TextFormatFlags.HorizontalCenter);
        }
    }
}
