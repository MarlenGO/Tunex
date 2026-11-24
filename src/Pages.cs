using System;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Tunex
{
    // Общие сервисы для страниц
    static class App
    {
        public static TweakManager Manager;
        public static MainForm Form;
        public static void Toast(string text, Color tint) { Form.ShowToast(text, tint, 5000); }
        public static void Toast(string text) { Form.ShowToast(text, Theme.Accent, 5000); }
        public static void Error(string text) { Form.ShowToast(text, Theme.Red, 8000); }

        public static string Pct(float v) { return v < 0 ? "—" : Math.Round(v).ToString(CultureInfo.InvariantCulture) + "%"; }
        public static string Deg(float v) { return v < 0 ? "—" : Math.Round(v).ToString(CultureInfo.InvariantCulture) + " °C"; }
        public static string Gb(float v) { return v < 0 ? "—" : v.ToString(v >= 10 ? "0" : "0.0", CultureInfo.InvariantCulture); }

        public static string Compare(Metrics a, Metrics b)
        {
            if (a == null || b == null) return "";
            List<string> parts = new List<string>();
            if (a.Cpu >= 0 && b.Cpu >= 0) parts.Add("ЦП " + Pct(a.Cpu) + " → " + Pct(b.Cpu));
            if (a.Gpu >= 0 && b.Gpu >= 0) parts.Add("ГП " + Pct(a.Gpu) + " → " + Pct(b.Gpu));
            if (a.Ram >= 0 && b.Ram >= 0) parts.Add("ОЗУ " + Pct(a.Ram) + " → " + Pct(b.Ram));
            if (a.GpuTemp >= 0 && b.GpuTemp >= 0) parts.Add("t° ГП " + Deg(a.GpuTemp) + " → " + Deg(b.GpuTemp));
            if (a.CpuTemp >= 0 && b.CpuTemp >= 0) parts.Add("t° ЦП " + Deg(a.CpuTemp) + " → " + Deg(b.CpuTemp));
            return parts.Count == 0 ? "Нет данных датчиков" : string.Join("   ·   ", parts.ToArray());
        }

        public static void ShowErrors(List<string> errors, string ok)
        {
            if (errors.Count > 0) Error("Не получилось:\n" + string.Join("\n", errors.ToArray()));
            else if (ok != null) Toast(ok, Theme.Green);
        }

        public static Card AdminCard()
        {
            Card c = new Card();
            FlatButton b = new FlatButton("Перезапустить", true, Icons.Shield);
            b.Click += delegate { Form.RestartAsAdmin(); };
            SettingRow r = new SettingRow("Часть настроек требует прав администратора",
                "Программа запущена без прав администратора, поэтому такие настройки выключены.", b);
            r.Glyph = Icons.Shield; r.GlyphColor = Theme.Orange;
            c.Add(r);
            return c;
        }
    }

    // ---------------------------------------------------------------- Процент оптимизации
    static class Optimization
    {
        static IEnumerable<Tweak> Considered()
        {
            foreach (KeyValuePair<string, Tweak> kv in Catalog.All) if (kv.Key != "session") yield return kv.Value;
        }

        public static int Percent()
        {
            int total = 0, on = 0;
            foreach (Tweak t in Considered())
            {
                if (t.Rec != Recommend.All) continue;
                TweakState s = TweakState.Of(t);
                if (!s.Available) continue;
                total++; if (s.On) on++;
            }
            return total == 0 ? 100 : on * 100 / total;
        }

        public static List<Color> Segments()
        {
            List<Color> l = new List<Color>();
            foreach (Tweak t in Considered())
            {
                if (t.Rec == Recommend.Optional) continue;
                TweakState s = TweakState.Of(t);
                if (s.Available) l.Add(s.Color);
            }
            return l;
        }
    }

    // ---------------------------------------------------------------- Главная
    class HomePage : Page
    {
        readonly PerfBlock perf;
        readonly Card benchCard, watchCard, checkCard, infoCard;
        readonly SettingRow benchRow, watchRow;
        readonly FlatButton benchBtn;
        readonly StatTile cpu, gpu, ram, vram;
        SettingRow sourcesRow;
        bool hwShown;

        public HomePage()
        {
            perf = Add(new PerfBlock());
            perf.StartButton.Click += delegate { App.Form.StartWizard(); };

            benchCard = Add(new Card());
            benchBtn = new FlatButton("Запустить проверку", true, Icons.Play);
            benchBtn.Click += delegate { App.Form.RunBench(); };
            benchRow = benchCard.Add(new SettingRow("Проверьте производительность ПК", "", benchBtn));

            watchCard = Add(new Card());
            watchRow = watchCard.Add(new SettingRow("", "", null));
            watchRow.Glyph = Icons.Eye;
            watchCard.Visible = false;

            Add(new SectionLabel("Мониторинг"));
            TileRow tiles = Add(new TileRow());
            cpu = new StatTile(Icons.Cpu, "ПРОЦЕССОР", Theme.Accent);
            gpu = new StatTile(Icons.Gpu, "ВИДЕОКАРТА", Theme.Blue);
            ram = new StatTile(Icons.Memory, "ОПЕРАТИВНАЯ ПАМЯТЬ", Theme.Purple);
            vram = new StatTile(Icons.Chip, "ВИДЕОПАМЯТЬ", Theme.Orange);
            tiles.AddTile(cpu); tiles.AddTile(gpu); tiles.AddTile(ram); tiles.AddTile(vram);

            Add(new SectionLabel("Что можно улучшить"));
            checkCard = Add(new Card());
            Add(new SectionLabel("Этот компьютер"));
            infoCard = Add(new Card());
        }

        public override void OnShow()
        {
            UpdatePerf();
            BuildChecks();
            BuildInfo();
            OnTick();
        }

        void UpdatePerf()
        {
            List<BenchResult> list = App.Manager.State.Benchmarks;
            BenchResult last = list.Count > 0 ? list[list.Count - 1] : null;
            perf.SetData(last, Optimization.Percent(), Optimization.Segments());
            if (last == null)
            {
                benchRow.Title = "Проверьте производительность ПК";
                benchRow.Description = "Займёт около 20 секунд. Узнаете, на что способен компьютер в играх, и сможете сравнить результат до и после настройки.";
                benchBtn.Text = "Запустить проверку";
            }
            else
            {
                benchRow.Title = "Игровой рейтинг: " + last.Total + " очков — " + Bench.Tier(last.Total).ToLowerInvariant() + " для игр";
                benchRow.Description = Bench.Verdict(last) + " Проверьте ещё раз после настройки, чтобы увидеть разницу.";
                benchBtn.Text = "Проверить снова";
            }
            benchCard.Relayout();
        }

        public override void OnTick()
        {
            SensorService s = App.Manager.Sensors;
            List<Sample> rec = s.Recent(120);
            Sample l = rec.Count > 0 ? rec[rec.Count - 1] : null;
            if (l != null)
            {
                cpu.Value = App.Pct(l.Cpu);
                cpu.Sub = l.CpuTemp >= 0 ? "Температура ≈ " + App.Deg(l.CpuTemp) : "Температура: нет датчика";
                gpu.Value = App.Pct(l.Gpu);
                gpu.Sub = l.GpuTemp >= 0 ? "Температура " + App.Deg(l.GpuTemp) : (SysInfo.HasNvidia ? "Температура: —" : "Температура: только NVIDIA");
                ram.Value = App.Gb(l.RamUsedGb) + " / " + App.Gb(l.RamTotalGb) + " ГБ";
                ram.Sub = "Занято " + App.Pct(l.Ram);
                if (l.VramUsedGb >= 0)
                {
                    vram.Value = App.Gb(l.VramUsedGb) + " / " + App.Gb(l.VramTotalGb) + " ГБ";
                    vram.Sub = l.VramShared ? "Встроенная графика" : "Выделенная память";
                }
                else { vram.Value = "—"; vram.Sub = "Нет данных"; }
            }
            int n = 120;
            float[] c = new float[n], g = new float[n], r = new float[n], v = new float[n];
            int off = n - rec.Count;
            for (int i = 0; i < n; i++)
            {
                Sample x = i >= off ? rec[i - off] : null;
                c[i] = x != null ? x.Cpu : -1; g[i] = x != null ? x.Gpu : -1; r[i] = x != null ? x.Ram : -1;
                v[i] = x != null && x.VramTotalGb > 0 ? x.VramUsedGb / x.VramTotalGb * 100f : -1;
            }
            cpu.Series = c; gpu.Series = g; ram.Series = r; vram.Series = v;
            List<float> mk = new List<float>();
            foreach (KeyValuePair<DateTime, string> m in s.Markers)
            {
                double ago = (DateTime.Now - m.Key).TotalSeconds;
                if (ago <= n) mk.Add((float)(1 - ago / n));
            }
            foreach (StatTile t in new StatTile[] { cpu, gpu, ram, vram }) { t.Markers = mk; t.Invalidate(); }

            UpdateWatch();
            if (sourcesRow != null && sourcesRow.Description != Sources()) { sourcesRow.Description = Sources(); sourcesRow.Invalidate(); }
            if (!hwShown && SysInfo.HardwareLoaded) { hwShown = true; BuildChecks(); BuildInfo(); perf.Invalidate(); }
        }

        void UpdateWatch()
        {
            TweakManager m = App.Manager;
            JournalEntry obs = m.Observing;
            JournalEntry last = m.State.Journal.Count > 0 ? m.State.Journal[0] : null;
            if (obs != null)
            {
                int left = Math.Max(0, TweakManager.ObserveSeconds - (int)(DateTime.Now - obs.When).TotalSeconds);
                watchRow.Title = "Слежу за системой после изменения: " + obs.Title;
                watchRow.Description = "Осталось " + left + " с. Сравню загрузку и температуру за минуту до и после. Отметка на графиках — момент изменения.";
                watchRow.GlyphColor = Theme.Orange;
                watchCard.Visible = true;
            }
            else if (last != null && last.After != null && !last.Undone && (DateTime.Now - last.When).TotalMinutes < 15)
            {
                watchRow.Title = "Результат: " + last.Title;
                watchRow.Description = App.Compare(last.Before, last.After) + "\nСравнение честное, только если нагрузка была одинаковой (например, в той же игре).";
                watchRow.GlyphColor = Theme.Green;
                watchCard.Visible = true;
            }
            else watchCard.Visible = false;
            if (watchCard.Visible) { watchCard.Relayout(); watchRow.Invalidate(); }
        }

        SettingRow Check(Card card, string glyph, Color color, string title, string desc, string button, Action fix)
        {
            FlatButton b = null;
            if (button != null) { b = new FlatButton(button, false); b.Click += delegate { fix(); }; }
            SettingRow r = new SettingRow(title, desc, b);
            r.Glyph = glyph; r.GlyphColor = color;
            return card.Add(r);
        }

        void Fix(string id)
        {
            Tweak t = Catalog.Resolve(id);
            App.Form.ApplyTweak(t, true, delegate { });
        }

        static bool IsOn(string id) { try { return Catalog.Resolve(id).ReadEnabled(); } catch { return true; } }

        void BuildChecks()
        {
            checkCard.SuspendLayout();
            checkCard.Clear();
            int issues = 0;

            foreach (MonitorInfo mon in Displays.Enumerate(false))
            {
                int max = mon.MaxHz;
                if (max > mon.Hz && mon.Hz > 0)
                {
                    issues++;
                    MonitorInfo mm = mon;
                    Check(checkCard, Icons.Display, Theme.Orange, "Экран работает на " + mon.Hz + " Гц, а может " + max + " Гц",
                        "Картинка в играх менее плавная, чем могла бы быть. Это частая и незаметная ошибка.", "Исправить", delegate
                        {
                            Tweak t = Catalog.Resolve("display.hz|" + mm.Device);
                            string err = App.Manager.ApplyAction(t, "Частота «" + mm.Name + "»: " + max + " Гц", delegate { Displays.SetHz(mm.Device, max); });
                            if (err != null) App.Error(err); else App.Toast("Частота экрана: " + max + " Гц", Theme.Green);
                            BuildChecks();
                        });
                }
            }

            if (Power.Active() == Power.Saver)
            {
                issues++;
                Check(checkCard, Icons.Power, Theme.Orange, "Включён режим экономии энергии", "Процессор работает медленнее — в играх будет меньше FPS.", "Открыть", delegate { App.Form.Navigate("power"); });
            }

            if (!IsOn("game.gamemode"))
            {
                issues++;
                Check(checkCard, Icons.Game, Theme.Red, "Игровой режим выключен", "Windows может ставить обновления прямо во время игры.", "Включить", delegate { Fix("game.gamemode"); });
            }
            if (!IsOn("game.gamebar"))
            {
                issues++;
                Check(checkCard, Icons.Game, Theme.Red, "Xbox записывает игру в фоне", "Фоновая запись клипов отнимает FPS.", "Отключить", delegate { Fix("game.gamebar"); });
            }
            if (SysInfo.IsAdmin && !IsOn("system.pagefile"))
            {
                issues++;
                Check(checkCard, Icons.Memory, Theme.Red, "Файл подкачки настроен вручную", "Если он слишком маленький, игры могут вылетать.", "Исправить", delegate { Fix("system.pagefile"); });
            }

            if (SysInfo.HardwareLoaded && SysInfo.Ram.Count > 0)
            {
                int speed = 0, conf = 0;
                foreach (RamModule m in SysInfo.Ram) { speed = Math.Max(speed, m.Speed); conf = Math.Max(conf, m.Configured); }
                if (SysInfo.Ram.Count == 1)
                {
                    issues++;
                    Check(checkCard, Icons.Memory, Theme.Orange, "Установлена одна планка памяти",
                        "С двумя одинаковыми планками память работает быстрее — во многих играх это +10–30% FPS.", null, null);
                }
                if (conf > 0 && speed > conf + 100)
                {
                    issues++;
                    Check(checkCard, Icons.Memory, Theme.Orange, "Память работает на " + conf + " МГц вместо " + speed,
                        "Скорее всего, в BIOS не включён профиль XMP (или EXPO). Его включают в настройках материнской платы.", null, null);
                }
            }

            try
            {
                DriveInfo d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                double freeGb = d.AvailableFreeSpace / 1073741824.0;
                if (freeGb < 15 || d.AvailableFreeSpace < d.TotalSize / 10)
                {
                    issues++;
                    Check(checkCard, Icons.Warning, Theme.Orange, "Мало места на диске C: " + freeGb.ToString("0", CultureInfo.InvariantCulture) + " ГБ",
                        "Когда диск почти заполнен, Windows и игры работают медленнее.", null, null);
                }
            }
            catch { }

            if (!SysInfo.IsAdmin)
                Check(checkCard, Icons.Shield, Theme.Text2, "Программа запущена без прав администратора", "Часть настроек будет недоступна.", "Перезапустить", delegate { App.Form.RestartAsAdmin(); });

            if (issues == 0)
                Check(checkCard, Icons.Check, Theme.Green, "Всё в порядке", "Частых проблем не нашлось.", null, null);
            checkCard.ResumeLayout();
            checkCard.Relayout();
        }

        void BuildInfo()
        {
            infoCard.Clear();
            Info(Icons.Cpu, "Процессор", SysInfo.CpuName);
            Info(Icons.Gpu, "Видеокарта", SysInfo.Gpus.Count > 0 ? string.Join(", ", SysInfo.Gpus.ToArray()) : (SysInfo.HardwareLoaded ? "—" : "Загрузка…"));
            Info(Icons.Memory, "Память", SysInfo.HardwareLoaded ? SysInfo.RamSummary() : "Загрузка…");
            Info(Icons.System, "Система", SysInfo.OsName);
            sourcesRow = Info(Icons.Thermo, "Откуда данные датчиков", Sources());
        }

        static string Sources()
        {
            SensorService s = App.Manager.Sensors;
            return "Видеокарта: " + (s.GpuSource == "" ? "нет данных" : s.GpuSource)
                + ". Температура процессора: " + (s.CpuTempSource == "" ? "датчик недоступен" : s.CpuTempSource) + ".";
        }

        SettingRow Info(string glyph, string title, string value)
        {
            SettingRow r = infoCard.Add(new SettingRow(title, value, null));
            r.Glyph = glyph; r.GlyphColor = Theme.Text2;
            return r;
        }
    }

    // ---------------------------------------------------------------- Оптимизация: все настройки ПК на одной странице
    class OptimizePage : Page
    {
        public static readonly string[] Sections = { "power", "game", "system", "net" };
        readonly List<TweakGrid> grids = new List<TweakGrid>();
        readonly Dictionary<string, Control> anchors = new Dictionary<string, Control>();
        readonly Card plans;

        public OptimizePage()
        {
            Add(new PageHeader(Icons.Speed, "Оптимизация", null, null));
            if (!SysInfo.IsAdmin) Add(App.AdminCard());
            JumpBar bar = Add(new JumpBar());
            bar.Add("power", Icons.Power, "Электропитание");
            bar.Add("game", Icons.Game, "Игры и графика");
            bar.Add("system", Icons.System, "Система");
            bar.Add("net", Icons.Network, "Сеть");
            bar.Jump += JumpTo;

            // Электропитание
            Heading("power", Icons.Power, "Электропитание", "схема питания и энергосбережение");
            plans = Add(new Card());
            Grid("power", "Энергосбережение", "Действует на текущую схему питания, когда компьютер работает от розетки.");

            // Игры и графика
            Heading("game", Icons.Game, "Игры и графика", "функции Windows для игр, видеокарта, мышь");
            Grid("game", "Настройки для игр", null);

            // Система
            Heading("system", Icons.System, "Система", "фоновые службы, реклама, память");
            Grid("system", "Настройки Windows", null);
            Card c = Add(new Card());
            Action(c, Icons.Memory, "Очистить оперативную память", "Убирает из памяти кэш старых файлов. Программы не закрываются. Полезно перед игрой, если памяти мало.",
                "Очистить", delegate(FlatButton btn)
                {
                    if (!SysInfo.IsAdmin) { App.Error("Нужны права администратора"); return; }
                    Background(btn, delegate
                    {
                        long freed; string err = MemoryTools.Clean(out freed);
                        return err != null ? "!" + err : "Память очищена: освобождено " + (freed >> 20) + " МБ";
                    });
                });
            Action(c, Icons.Delete, "Очистить кэш шейдеров", "Помогает от фризов после обновления драйвера видеокарты. Первый запуск игр после этого будет чуть дольше.",
                "Очистить", delegate(FlatButton btn)
                {
                    Background(btn, delegate { return "Кэш шейдеров очищен: " + (Maintenance.ClearShaderCache() >> 20) + " МБ"; });
                });

            // Сеть
            Heading("net", Icons.Network, "Сеть", "стабильный пинг без лишних задержек");
            Grid("net", "Настройки сети", "Помогают от скачков пинга. Сам пинг до сервера зависит от провайдера и расстояния — его программа не изменит.");
            Card n = Add(new Card());
            Action(n, Icons.Refresh, "Сбросить DNS-кэш", "Помогает, если сайт или лаунчер игры перестал открываться, а у других всё работает.",
                "Сбросить", delegate(FlatButton btn) { Background(btn, delegate { return NetTools.FlushDns() ? "DNS-кэш сброшен" : "!Не удалось сбросить DNS-кэш"; }); });
        }

        void Heading(string key, string glyph, string title, string subtitle)
        {
            SectionHeading h = new SectionHeading(glyph, title, subtitle);
            h.Number = (anchors.Count + 1).ToString("00");
            Add(h);
            h.Margin = new Padding(0, Theme.S(14), 0, Theme.S(8));
            anchors[key] = h;
        }

        void Grid(string cat, string title, string subtitle)
        {
            TweakGrid g = Add(new TweakGrid(title, subtitle));
            List<Tweak> list = Catalog.InCategory(cat);
            foreach (Recommend r in new Recommend[] { Recommend.All, Recommend.Some, Recommend.Optional })
                foreach (Tweak t in list) if (t.Rec == r) g.AddTweak(t);
            grids.Add(g);
        }

        // Прокрутить к разделу
        public void JumpTo(string key)
        {
            Control c;
            if (!anchors.TryGetValue(key, out c)) return;
            PerformLayout();
            int y = c.Top - AutoScrollPosition.Y - Theme.S(6);
            AutoScrollPosition = new Point(0, Math.Max(0, y));
        }

        public override void OnShow()
        {
            BuildPlans();
            foreach (TweakGrid g in grids) g.SyncAll();
        }

        static SettingRow Action(Card c, string glyph, string title, string desc, string button, Action<FlatButton> run)
        {
            FlatButton b = new FlatButton(button, false);
            b.Click += delegate { run(b); };
            SettingRow r = c.Add(new SettingRow(title, desc, b));
            r.Glyph = glyph; r.GlyphColor = Theme.Accent;
            return r;
        }

        // Выполнить в фоне и показать результат
        static void Background(FlatButton b, Func<string> work)
        {
            b.Enabled = false;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string msg;
                try { msg = work(); } catch (Exception ex) { msg = "!" + ex.Message; }
                App.Form.BeginInvoke(new Action(delegate
                {
                    b.Enabled = true;
                    if (msg.StartsWith("!")) App.Error(msg.Substring(1)); else App.Toast(msg, Theme.Green);
                }));
            });
        }

        void BuildPlans()
        {
            plans.SuspendLayout();
            plans.Clear();
            Guid active = Power.Active();
            string ultimateName = Power.Name(Power.Ultimate);
            bool hasOwn = false;
            foreach (Guid g in Power.Schemes())
            {
                string name = Power.Name(g);
                bool own = Power.IsOwn(g, name) || (ultimateName != "" && name == ultimateName);
                if (own) hasOwn = true;
                Control right;
                if (g == active) right = new Pill("Активна", Theme.Green);
                else
                {
                    FlatButton b = new FlatButton("Выбрать", false);
                    Guid gg = g; string nn = name;
                    b.Click += delegate { Activate(gg, nn); };
                    right = b;
                }
                SettingRow r = plans.Add(new SettingRow(name == "" ? g.ToString() : name, Power.Describe(own ? Power.Ultimate : g), right));
                r.Glyph = Icons.Power; r.GlyphColor = g == active ? Theme.Green : Theme.Text2;
            }
            if (!hasOwn)
            {
                FlatButton add = new FlatButton("Создать и включить", true, Icons.Add);
                add.Click += delegate
                {
                    Guid g;
                    try { g = Power.CreateMaxPerformance(); }
                    catch (Exception ex) { App.Error("Не удалось создать схему: " + ex.Message); return; }
                    Activate(g, Power.OwnName);
                };
                SettingRow r = plans.Add(new SettingRow("Максимальная производительность",
                    "Схема без экономии энергии: процессор не сбрасывает частоту, ядра не засыпают, видеокарта и USB всегда активны.", add));
                r.Glyph = Icons.Add; r.GlyphColor = Theme.Accent;
            }

            // «Режим питания» — ползунок из Параметров Windows; на ноутбуках работает поверх схемы «Сбалансированная»
            if (Power.OverlaySupported() && active == Power.Balanced)
            {
                DarkDropdown mode = new DarkDropdown();
                mode.Width = Theme.S(250);
                Guid[] ids = { Power.OverlayBest, Power.OverlayBalanced, Power.OverlayBattery };
                mode.Items.Add("Макс. производительность"); mode.Items.Add("Сбалансированный"); mode.Items.Add("Экономия заряда");
                Guid cur = Guid.Empty; try { cur = Power.Overlay(); } catch { }
                mode.SelectedIndex = Array.IndexOf(ids, cur);
                mode.SelectedIndexChanged += delegate
                {
                    Guid target = ids[mode.SelectedIndex];
                    string label = mode.Items[mode.SelectedIndex];
                    string err = App.Manager.ApplyAction(Catalog.PowerOverlay, "Режим питания: " + label, delegate { Power.SetOverlay(target); });
                    if (err != null) App.Error(err); else App.Toast("Режим питания: " + label, Theme.Green);
                    OnShow();
                };
                SettingRow r = plans.Add(new SettingRow("Режим питания",
                    "Тот же ползунок, что в Параметрах Windows → Система → Питание. Для игр выберите «Макс. производительность».", mode));
                r.Glyph = Icons.Power; r.GlyphColor = cur == Power.OverlayBest ? Theme.Green : Theme.Orange;
            }

            plans.Footer = Power.IsModernStandby()
                ? "На этом ноутбуке современный режим сна. Windows показывает в Панели управления только «Сбалансированную», но схемы из этого списка работают — Tunex переключает их напрямую. Предыдущая схема сохраняется в «Журнале»."
                : "Схема меняется сразу. Предыдущая сохраняется в «Журнале», её можно вернуть.";
            plans.ResumeLayout();
            plans.Relayout();
        }

        void Activate(Guid g, string name)
        {
            string err = App.Manager.ApplyAction(Catalog.PowerPlan, "Схема питания: " + name, delegate { Power.SetActive(g); });
            if (err != null) App.Error(err);
            else if (Power.Active() != g) App.Error("Windows не переключила схему. Попробуйте перезапустить Tunex от администратора.");
            else App.Toast("Включена схема «" + name + "». Проверить: powercfg /getactivescheme", Theme.Green);
            OnShow();
        }
    }

    // ---------------------------------------------------------------- Игры
    class GamesPage : Page
    {
        readonly List<TweakGrid> grids = new List<TweakGrid>();

        public GamesPage() { Rebuild(); }

        void Rebuild()
        {
            SuspendLayout();
            Clear();
            grids.Clear();
            FlatButton add = new FlatButton("Добавить игру", true, Icons.Add);
            add.Click += delegate { AddGame(); };
            Add(new PageHeader(Icons.Library, "Игры", "Добавьте свои игры: Tunex настроит их запуск и поможет, пока вы играете.", add));
            if (!SysInfo.IsAdmin) Add(App.AdminCard());

            TweakGrid session = Add(new TweakGrid("Во время игры", "Срабатывает, когда запущена игра из списка ниже."));
            foreach (Tweak t in Catalog.InCategory("session")) session.AddTweak(t);
            grids.Add(session);

            List<string> games = App.Manager.State.Games;
            if (games.Count == 0)
            {
                Card c = Add(new Card());
                SettingRow r = c.Add(new SettingRow("Пока нет игр",
                    "Нажмите «Добавить игру» и выберите файл игры (.exe). Для Steam он обычно лежит в папке steamapps\\common\\<игра>.", null));
                r.Glyph = Icons.Game; r.GlyphColor = Theme.Text2;
            }
            foreach (string exe in games)
            {
                IconButton del = new IconButton(Icons.Delete, Theme.S(34));
                string path = exe;
                del.Click += delegate { RemoveGame(path); };
                TweakGrid g = Add(new TweakGrid(Path.GetFileNameWithoutExtension(exe), File.Exists(exe) ? exe : exe + "  (файл не найден)"));
                g.Accessory = del;
                foreach (string kind in new string[] { "game.gpu", "game.prio", "game.fso" }) g.AddTweak(Catalog.GameTweak(kind, exe));
                grids.Add(g);
            }
            ResumeLayout();
            FitWidths();
        }

        void AddGame()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Filter = "Игра (*.exe)|*.exe"; d.Title = "Выберите файл игры";
                if (d.ShowDialog(App.Form) != DialogResult.OK) return;
                List<string> games = App.Manager.State.Games;
                foreach (string g in games) if (string.Equals(g, d.FileName, StringComparison.OrdinalIgnoreCase)) return;
                games.Add(d.FileName);
                App.Manager.State.Save();
                Rebuild();
            }
        }

        void RemoveGame(string exe)
        {
            if (!DarkDialog.Ask(App.Form, "Убрать игру из списка?", Path.GetFileName(exe) + "\nНастройки, включённые для неё, будут сброшены.", "Убрать", "Отмена")) return;
            List<string> errors = new List<string>();
            foreach (string kind in new string[] { "game.gpu", "game.fso", "game.prio" })
            {
                Tweak t = Catalog.GameTweak(kind, exe);
                bool on = false; try { on = t.ReadEnabled(); } catch { }
                if (on) { string err = App.Manager.ApplyToggleNow(t, false); if (err != null) errors.Add(t.Title + ": " + err); }
            }
            App.Manager.State.Games.Remove(exe);
            App.Manager.State.Save();
            App.ShowErrors(errors, null);
            Rebuild();
        }

        public override void OnShow() { foreach (TweakGrid g in grids) g.SyncAll(); }
    }

    // ---------------------------------------------------------------- Экран
    class DisplayPage : Page
    {
        bool built;
        readonly List<MonitorInfo> monitors = new List<MonitorInfo>();

        public override void OnShow() { if (!built) Rebuild(); }

        void Rebuild()
        {
            built = true;
            SuspendLayout();
            Clear();
            FlatButton refresh = new FlatButton("Обновить", false, Icons.Refresh);
            refresh.Click += delegate { Rebuild(); };
            Add(new PageHeader(Icons.Display, "Экран", "Яркость, контраст, гамма и частота обновления для каждого монитора.", refresh));
            monitors.Clear();
            monitors.AddRange(Displays.Enumerate(true));
            foreach (MonitorInfo m in monitors) AddMonitor(m);
            if (monitors.Count == 0)
            {
                Card c = Add(new Card());
                c.Add(new SettingRow("Мониторы не найдены", null, null));
            }
            ResumeLayout();
            FitWidths();
        }

        void AddMonitor(MonitorInfo m)
        {
            Add(new SectionLabel(m.Name + (m.Primary ? " · основной" : "")));
            Card c = Add(new Card());
            c.Footer = "Яркость и контраст меняются в самом мониторе (DDC/CI). Гамма сбрасывается после перезагрузки или смены режима экрана.";

            SettingRow head = c.Add(new SettingRow(m.Bounds.Width + " × " + m.Bounds.Height + " · " + m.Hz + " Гц", m.Device.Replace(@"\\.\", ""), null));
            head.Glyph = Icons.Display; head.GlyphColor = Theme.Accent;

            // Частота
            DarkDropdown hz = new DarkDropdown();
            foreach (int r in m.Rates) hz.Items.Add(r + " Гц");
            hz.SelectedIndex = m.Rates.IndexOf(m.Hz);
            SettingRow hzRow = c.Add(new SettingRow("Частота обновления", null, hz));
            if (m.MaxHz > m.Hz)
            {
                hzRow.Description = "Монитор поддерживает до " + m.MaxHz + " Гц — выберите максимум для плавной картинки.";
                hzRow.DescriptionColor = Theme.Orange;
                hzRow.Badges.Add(new Badge("Не максимум", Theme.Orange));
            }
            else hzRow.Description = "Сейчас стоит максимальная частота.";
            hz.SelectedIndexChanged += delegate
            {
                int target = m.Rates[hz.SelectedIndex];
                Tweak t = Catalog.Resolve("display.hz|" + m.Device);
                string err = App.Manager.ApplyAction(t, "Частота «" + m.Name + "»: " + target + " Гц", delegate { Displays.SetHz(m.Device, target); });
                if (err != null) App.Error(err); else App.Toast("Частота: " + target + " Гц. Отменить можно в «Журнале».", Theme.Green);
                Rebuild();
            };

            // Яркость
            if (m.Ddc || m.Wmi)
            {
                DarkSlider b = Slider(m.BMin, m.BMax, m.BCur, delegate(int v) { Displays.SetBrightness(m, v); });
                SettingRow r = c.Add(new SettingRow("Яркость", m.Wmi ? "Встроенный экран ноутбука" : null, b));
                r.Glyph = Icons.Brightness; r.GlyphColor = Theme.Orange;
            }
            else
            {
                SettingRow r = c.Add(new SettingRow("Яркость и контраст недоступны",
                    "Монитор не отвечает по DDC/CI. Включите DDC/CI в меню самого монитора и нажмите «Обновить».", null));
                r.Glyph = Icons.Brightness; r.GlyphColor = Theme.Text3;
            }

            if (m.HasContrast)
            {
                DarkSlider ct = Slider(m.CMin, m.CMax, m.CCur, delegate(int v) { Displays.SetContrast(m, v); });
                SettingRow r = c.Add(new SettingRow("Контраст", null, ct));
                r.Glyph = Icons.Brightness; r.GlyphColor = Theme.Text2;
            }

            // Гамма
            DarkSlider gm = Slider(50, 200, 100, null);
            gm.Format = delegate(int v) { return (v / 100.0).ToString("0.00", CultureInfo.InvariantCulture); };
            Debounce(gm, delegate(int v)
            {
                if (!Displays.SetGamma(m.Device, v / 100.0))
                    App.Form.BeginInvoke(new Action(delegate { App.Error("Windows ограничивает такой диапазон гаммы. Попробуйте значение ближе к 1.00."); }));
            });
            IconButton reset = new IconButton(Icons.Undo, Theme.S(32));
            Panel holder = new Panel();
            holder.BackColor = Color.Transparent;
            holder.Size = new Size(gm.Width + reset.Width + Theme.S(8), gm.Height);
            gm.Location = new Point(0, 0);
            reset.Location = new Point(gm.Width + Theme.S(8), (gm.Height - reset.Height) / 2);
            reset.Click += delegate { gm.Value = 100; Displays.SetGamma(m.Device, 1.0); };
            holder.Controls.Add(gm); holder.Controls.Add(reset);
            SettingRow gr = c.Add(new SettingRow("Гамма", "Меньше 1 — темнее тени, больше 1 — светлее. Помогает разглядеть врагов в тёмных местах.", holder));
            gr.Glyph = Icons.Eye; gr.GlyphColor = Theme.Purple;
        }

        static DarkSlider Slider(int min, int max, int value, Action<int> apply)
        {
            DarkSlider s = new DarkSlider();
            s.Minimum = min; s.Maximum = max; s.Value = value;
            if (apply != null) Debounce(s, apply);
            return s;
        }

        // DDC/CI медленный: отправляем последнее значение не чаще раза в 150 мс, в фоне
        static void Debounce(DarkSlider s, Action<int> apply)
        {
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 150;
            int busy = 0;
            t.Tick += delegate
            {
                t.Stop();
                if (Interlocked.Exchange(ref busy, 1) == 1) { t.Start(); return; }
                int v = s.Value;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { apply(v); } catch { }
                    Interlocked.Exchange(ref busy, 0);
                });
            };
            s.ValueChanged += delegate { t.Stop(); t.Start(); };
            s.Disposed += delegate { t.Dispose(); };
        }
    }

    // ---------------------------------------------------------------- NVIDIA
    class NvidiaPage : Page
    {
        public NvidiaPage()
        {
            Add(new PageHeader(Icons.Chip, "NVIDIA", "Видеокарта, оверлеи и программы, которые работают рядом с игрой.", null));
        }

        bool built;
        TweakGrid discordGrid;
        SettingRow overlayRow;
        int tickN;

        public override void OnShow()
        {
            if (discordGrid != null) discordGrid.SyncAll();
            UpdateOverlay();
            if (built || !SysInfo.HardwareLoaded) return;
            built = true;
            SuspendLayout();

            // ---- Видеокарта
            Card c = Add(new Card());
            if (!SysInfo.HasNvidia)
            {
                SettingRow r = c.Add(new SettingRow("Видеокарта NVIDIA не найдена",
                    "На этом ПК: " + (SysInfo.Gpus.Count > 0 ? string.Join(", ", SysInfo.Gpus.ToArray()) : "—") + ". Настройки NVIDIA заработают на компьютере с NVIDIA, раздел Discord — уже сейчас.", null));
                r.Glyph = Icons.Info; r.GlyphColor = Theme.Text2;
            }
            else
            {
                FlatButton open = new FlatButton("Открыть", false);
                open.Click += delegate
                {
                    try { Process.Start("explorer.exe", @"shell:AppsFolder\NVIDIACorp.NVIDIAControlPanel_56jybvy8sckqj!NVIDIACorp.NVIDIAControlPanel"); }
                    catch (Exception ex) { App.Error(ex.Message); }
                };
                SettingRow head = c.Add(new SettingRow("Панель управления NVIDIA", string.Join(", ", SysInfo.Gpus.ToArray()), open));
                head.Glyph = Icons.Chip; head.GlyphColor = Theme.Green;

                // ---- Оверлей NVIDIA: ShadowPlay, игровой фильтр, фото-режим
                Add(new SectionLabel("Оверлей и запись NVIDIA"));
                Card oc = Add(new Card());
                string app = NvidiaOverlay.AppPath();
                if (app == null)
                {
                    overlayRow = oc.Add(new SettingRow("Оверлей NVIDIA, ShadowPlay, игровой фильтр, фото-режим",
                        "NVIDIA App и GeForce Experience не установлены — оверлея и записи на этом ПК нет, отключать нечего.", new Pill("Не установлено", Theme.Green)));
                }
                else
                {
                    bool newApp = NvidiaOverlay.IsNewApp(app);
                    FlatButton ob = new FlatButton(newApp ? "Открыть NVIDIA App" : "Открыть GeForce Experience", false);
                    ob.Click += delegate { try { Process.Start(app); } catch (Exception ex) { App.Error(ex.Message); } };
                    overlayRow = oc.Add(new SettingRow("Отключить оверлей NVIDIA, ShadowPlay, игровой фильтр и фото-режим",
                        "Запись (мгновенный повтор), игровой фильтр и фото-режим работают через оверлей — выключите его, и они выключатся вместе с ним. " +
                        "NVIDIA не разрешает менять это из других программ, поэтому один раз переключите в приложении: " +
                        (newApp ? "Настройки → Функции → Оверлей NVIDIA → выкл." : "Настройки → Общие → Внутриигровой оверлей → выкл."), ob));
                    oc.Footer = "Метка рядом с названием показывает, работает ли оверлей прямо сейчас. Tunex проверяет это каждые несколько секунд.";
                }
                overlayRow.Glyph = Icons.Eye; overlayRow.GlyphColor = Theme.Text2;
            }

            // ---- Discord
            Add(new SectionLabel("Discord"));
            discordGrid = Add(new TweakGrid(null, null));
            foreach (Tweak t in Catalog.InCategory("apps")) discordGrid.AddTweak(t);
            Card dc = Add(new Card());
            FlatButton db = new FlatButton("Открыть Discord", false);
            db.Enabled = DiscordHwTweak.Installed;
            db.Click += delegate { try { DiscordHwTweak.Open(); } catch (Exception ex) { App.Error(ex.Message); } };
            SettingRow dr = dc.Add(new SettingRow("Отключить оверлей Discord",
                "Оверлей рисуется поверх игры и может снижать FPS. Discord хранит эту настройку внутри своего аккаунта, другие программы поменять её не могут — " +
                "переключите один раз: Настройки пользователя → Оверлей в игре → выключите «Включить внутриигровой оверлей».", db));
            dr.Glyph = Icons.Eye; dr.GlyphColor = Theme.Text2;

            // ---- Скоро
            if (SysInfo.HasNvidia)
            {
                Add(new SectionLabel("Скоро"));
                Card s = Add(new Card());
                s.Footer = "Эти настройки появятся в следующей версии: они пишутся в профиль драйвера через NVAPI, и их нужно проверить на ПК с NVIDIA.";
                string[][] items =
                {
                    new string[] { "Размер кэша шейдеров: 10 ГБ", "Меньше подгрузок и фризов при первом запуске уровней." },
                    new string[] { "Режим низкой задержки: Ультра", "Меньше задержка ввода, если в игре нет NVIDIA Reflex." },
                    new string[] { "Управление питанием: максимальная производительность", "Видеокарта не сбрасывает частоты в лёгких сценах." },
                    new string[] { "Фильтрация текстур: высокая производительность", "Пара процентов FPS почти без потери качества." },
                    new string[] { "Цифровая яркость (насыщенность)", "Сочнее цвета, проще заметить противника." }
                };
                foreach (string[] it in items)
                {
                    SettingRow r = s.Add(new SettingRow(it[0], it[1], null));
                    r.Badges.Add(new Badge("Скоро", Theme.Text2));
                }
            }
            ResumeLayout();
            UpdateOverlay();
        }

        // Живая метка: запущен ли сейчас оверлей NVIDIA
        void UpdateOverlay()
        {
            if (overlayRow == null || NvidiaOverlay.AppPath() == null) return;
            bool on = NvidiaOverlay.Running();
            overlayRow.Badges.Clear();
            overlayRow.Badges.Add(on ? new Badge("Сейчас включён", Theme.Orange) : new Badge("Сейчас выключен", Theme.Green));
            overlayRow.GlyphColor = on ? Theme.Orange : Theme.Green;
            overlayRow.Invalidate();
        }

        public override void OnTick()
        {
            if (!built && SysInfo.HardwareLoaded) { OnShow(); return; }
            if (++tickN % 4 == 0) UpdateOverlay();
        }
    }

    // ---------------------------------------------------------------- Оценка ПК: вкладки «Узкие места», «Игры», «Характеристики»
    class PcPage : Page
    {
        readonly JumpBar tabs;
        readonly Card findCard, specCard, searchCard, resultCard, gamesCard, detailCard;
        readonly TableView popTable, fpsTable;
        readonly SectionLabel popLabel, mineLabel;
        readonly FlatButton again, find;
        readonly DarkTextBox query;
        readonly Dictionary<string, List<Control>> tabControls = new Dictionary<string, List<Control>>();
        readonly List<Control> gamesMain = new List<Control>(), gamesDetail = new List<Control>();
        readonly Dictionary<int, GameReq> reqs = new Dictionary<int, GameReq>();
        string tab = "find";
        bool loaded, loading, resultOpen;
        PopularGame detail;
        int detailCpu;               // 0 — процессор этого ПК, дальше — FpsData.TableCpus
        PcProfile profile;
        int gamesGen, popGen;
        string findTitle = "Узкие места";

        public PcPage()
        {
            again = new FlatButton("Проверить заново", false, Icons.Refresh);
            again.Click += delegate { Load(); };
            Add(new PageHeader(Icons.Cpu, "Оценка ПК", null, again));
            tabs = Add(new JumpBar());
            tabs.Add("find", Icons.Warning, findTitle);
            tabs.Add("games", Icons.Library, "Игры");
            tabs.Add("spec", Icons.Cpu, "Характеристики");
            tabs.Jump += ShowTab;
            tabs.Margin = new Padding(0, Theme.S(4), 0, Theme.S(16));

            // ---- Узкие места
            findCard = Add(new Card());
            Tab("find", findCard);

            // ---- Игры: популярные
            popLabel = Add(new SectionLabel("Популярные игры"));
            popTable = Add(new TableView());
            popTable.Clickable = true;
            popTable.Columns.Add(new TableColumn("Игра", 3.2f, false));
            popTable.Columns.Add(new TableColumn("Жанр", 1.8f, false));
            popTable.Columns.Add(new TableColumn("Требования Steam", 2.0f, false));
            popTable.Columns.Add(new TableColumn("≈ FPS низкие", 1.3f, true));
            popTable.Columns.Add(new TableColumn("≈ FPS высокие", 1.3f, true));
            popTable.Footer = "FPS — примерная оценка для этого ПК в Full HD, а не замер. Нажмите на игру, чтобы увидеть FPS на других видеокартах и процессорах.";
            popTable.RowClick += delegate(int i) { ShowDetail(FpsData.Games[i]); };
            gamesMain.Add(popLabel); gamesMain.Add(popTable);

            // ---- Игры: свои и поиск
            mineLabel = Add(new SectionLabel("Ваши игры Steam и поиск"));
            searchCard = Add(new Card());
            query = new DarkTextBox(Theme.S(300), "Название игры, например Rust");
            find = new FlatButton("Найти", false, Icons.Search);
            Panel holder = new Panel();
            holder.BackColor = Color.Transparent;
            holder.Size = new Size(query.Width + Theme.S(8) + find.Width, Math.Max(query.Height, find.Height));
            query.Location = new Point(0, (holder.Height - query.Height) / 2);
            find.Location = new Point(query.Width + Theme.S(8), (holder.Height - find.Height) / 2);
            holder.Controls.Add(query); holder.Controls.Add(find);
            find.Click += delegate { Search(); };
            query.Submit += Search;
            SettingRow sr = searchCard.Add(new SettingRow("Проверить другую игру", "Найдите игру в магазине Steam — даже ту, что ещё не куплена.", holder));
            sr.Glyph = Icons.Search; sr.GlyphColor = Theme.Text2;
            resultCard = Add(new Card());
            gamesCard = Add(new Card());
            gamesMain.Add(mineLabel); gamesMain.Add(searchCard); gamesMain.Add(resultCard); gamesMain.Add(gamesCard);

            // ---- Игры: таблица FPS одной игры
            detailCard = Add(new Card());
            fpsTable = Add(new TableView());
            fpsTable.Columns.Add(new TableColumn("Видеокарта", 3.0f, false));
            fpsTable.Columns.Add(new TableColumn("Оценка", 1.0f, true));
            fpsTable.Columns.Add(new TableColumn("1080p низкие", 1.4f, true));
            fpsTable.Columns.Add(new TableColumn("1080p высокие", 1.4f, true));
            fpsTable.Columns.Add(new TableColumn("1440p высокие", 1.4f, true));
            gamesDetail.Add(detailCard); gamesDetail.Add(fpsTable);
            List<Control> g = new List<Control>(gamesMain); g.AddRange(gamesDetail);
            tabControls["games"] = g;

            // ---- Характеристики
            specCard = Add(new Card());
            Tab("spec", specCard);

            ShowTab("find");
            // Для проверки интерфейса: --pctab games, --pcgame <номер популярной игры>
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 1; i < args.Length - 1; i++)
            {
                int n;
                if (args[i] == "--pctab" && tabControls.ContainsKey(args[i + 1])) ShowTab(args[i + 1]);
                if (args[i] == "--pcgame" && int.TryParse(args[i + 1], out n) && n >= 0 && n < FpsData.Games.Length) { ShowTab("games"); ShowDetail(FpsData.Games[n]); }
            }
        }

        void Tab(string key, Control c)
        {
            if (!tabControls.ContainsKey(key)) tabControls[key] = new List<Control>();
            tabControls[key].Add(c);
        }

        void ShowTab(string key)
        {
            tab = key; tabs.Selected = key; tabs.Invalidate();
            SuspendLayout();
            foreach (KeyValuePair<string, List<Control>> kv in tabControls)
                foreach (Control c in kv.Value) c.Visible = false;
            if (key == "games")
            {
                foreach (Control c in detail == null ? gamesMain : gamesDetail) c.Visible = true;
                if (detail == null) resultCard.Visible = resultOpen;
            }
            else foreach (Control c in tabControls[key]) c.Visible = true;
            ResumeLayout();
            AutoScrollPosition = new Point(0, 0);
        }

        public override void OnShow() { if (!loaded && !loading) Load(); }

        void Load()
        {
            loading = true; again.Enabled = false;
            findCard.Clear(); specCard.Clear();
            SettingRow wait = findCard.Add(new SettingRow("Собираю сведения о компьютере…", "Это занимает несколько секунд.", null));
            wait.Glyph = Icons.Refresh; wait.GlyphColor = Theme.Text3;
            ThreadPool.QueueUserWorkItem(delegate
            {
                PcSpecs s = null; List<Finding> f = null; string err = null;
                try { s = PcCheck.Collect(); f = PcCheck.Analyze(s); } catch (Exception ex) { err = ex.Message; }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        loading = false; loaded = true; again.Enabled = true;
                        if (err != null) { App.Error("Не удалось собрать сведения: " + err); return; }
                        Fill(s, f);
                    }));
                }
                catch { }
            });
        }

        void Fill(PcSpecs s, List<Finding> list)
        {
            SuspendLayout();
            findCard.Clear(); specCard.Clear();

            // ---- Узкие места
            int crit = 0, warn = 0, info = 0;
            foreach (Finding x in list) { if (x.Level == Severity.Critical) crit++; else if (x.Level == Severity.Warning) warn++; else info++; }
            if (crit + warn == 0)
            {
                SettingRow ok = findCard.Add(new SettingRow("Серьёзных узких мест не нашлось",
                    "Память, диск, экран и питание настроены правильно." + (info > 0 ? " Ниже — советы." : ""), null));
                ok.Glyph = Icons.Check; ok.GlyphColor = Theme.Green;
            }
            else
            {
                SettingRow sum = findCard.Add(new SettingRow("Мешают играм: " + crit + " · стоит исправить: " + warn + (info > 0 ? " · к сведению: " + info : ""), null, null));
                sum.Glyph = Icons.Warning; sum.GlyphColor = crit > 0 ? Theme.Red : Theme.Orange;
            }
            foreach (Finding x in list)
            {
                FlatButton b = null;
                if (x.Fix != null)
                {
                    Finding xx = x;
                    b = new FlatButton(x.Button ?? "Исправить", x.Level == Severity.Critical);
                    b.Click += delegate { xx.Fix(); if (xx.Glyph == Icons.Display || xx.Glyph == Icons.Power) Load(); };
                }
                SettingRow r = findCard.Add(new SettingRow(x.Title, x.Text, b));
                Color c = x.Level == Severity.Critical ? Theme.Red : x.Level == Severity.Warning ? Theme.Orange : Theme.Text2;
                r.Glyph = x.Glyph; r.GlyphColor = c;
                r.Badges.Add(new Badge(x.Level == Severity.Critical ? "Мешает играм" : x.Level == Severity.Warning ? "Стоит исправить" : "К сведению", c));
            }
            findCard.Footer = "Tunex сообщает только о том, что может определить надёжно. Температуру и перегрев покажет стресс-тест — он появится позже.";

            // ---- Характеристики
            Spec(Icons.Cpu, "Процессор", s.Cpu,
                (s.Cores > 0 ? Plural(s.Cores, "ядро", "ядра", "ядер") + " · " : "") + Plural(s.Threads, "поток", "потока", "потоков") +
                (s.BaseMHz > 0 ? " · базовая частота " + (s.BaseMHz / 1000.0).ToString("0.0#", CultureInfo.InvariantCulture) + " ГГц" : ""));

            foreach (GpuInfo g in s.Gpus)
            {
                string d = (g.Integrated ? "встроенная" : "отдельная");
                if (!g.Integrated && SysInfo.VramBytes > 0) d += " · " + PcCheck.Gb(SysInfo.VramBytes) + " видеопамяти";
                d += " · оценка " + g.Score + (g.Known ? "" : " (модель неизвестна, примерно)");
                if (!string.IsNullOrEmpty(g.DriverVersion)) d += " · драйвер " + g.DriverVersion + (g.DriverDate != null ? " от " + g.DriverDate.Value.ToString("dd.MM.yyyy") : "");
                Spec(Icons.Gpu, "Видеокарта", g.Name, d);
            }

            if (s.Ram.Count > 0)
            {
                string type = ""; int speed = 0; bool same = true; string key0 = s.Ram[0].ChannelKey; bool oneCh = key0 != null;
                List<string> makers = new List<string>();
                foreach (MemInfo m in s.Ram)
                {
                    if (m.TypeName != "") type = m.TypeName;
                    if (m.Configured > 0) speed = m.Configured; else if (speed == 0) speed = m.Speed;
                    if (m.Capacity != s.Ram[0].Capacity) same = false;
                    if (m.ChannelKey != key0) oneCh = false;
                    if (m.Maker != "" && !makers.Contains(m.Maker)) makers.Add(m.Maker);
                }
                string layout = same ? s.Ram.Count + " × " + PcCheck.Gb(s.Ram[0].Capacity) : s.Ram.Count + " планки разного объёма";
                string mode = s.Ram.Count == 1 ? "одноканальный режим" : (oneCh && !s.Laptop) ? "один канал" : "двухканальный режим";
                Spec(Icons.Memory, "Оперативная память", PcCheck.Gb(s.RamTotal) + (type != "" ? " " + type : ""),
                    layout + (speed > 0 ? " · " + speed + " МГц" : "") + " · " + mode + (makers.Count > 0 ? " · " + string.Join(", ", makers.ToArray()) : ""));
            }

            foreach (DiskInfo d in s.Disks)
                Spec(Icons.System, d.System ? "Системный диск" : "Диск", d.Name,
                    d.Kind + " · " + PcCheck.Gb(d.Size) + (d.Free >= 0 ? " · свободно " + PcCheck.Gb(d.Free) : ""));

            foreach (MonitorInfo m in s.Monitors)
                Spec(Icons.Display, m.Primary ? "Основной экран" : "Экран", m.Name,
                    m.Bounds.Width + "×" + m.Bounds.Height + " · " + m.Hz + " Гц" + (m.MaxHz > m.Hz ? " (может " + m.MaxHz + " Гц)" : m.MaxHz > 0 ? " — максимум" : ""));

            Spec(Icons.Home, "Система", SysInfo.OsName,
                s.Laptop ? "Ноутбук · " + (s.OnBattery ? "работает от батареи" : "работает от сети") : "Настольный компьютер");

            profile = PcProfile.From(s);
            FillPopular();
            LoadPopularReqs();
            LoadGames();
            if (detail != null) ShowDetail(detail);
            ResumeLayout();
            PerformLayout();
        }

        void Spec(string glyph, string kind, string name, string details)
        {
            SettingRow r = specCard.Add(new SettingRow(name, details, null));
            r.Glyph = glyph; r.GlyphColor = Theme.Text2;
            r.Badges.Add(new Badge(kind, Theme.Text3));
        }

        static string Plural(int n, string one, string few, string many)
        {
            int m10 = n % 10, m100 = n % 100;
            string w = m10 == 1 && m100 != 11 ? one : m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14) ? few : many;
            return n + " " + w;
        }

        // ---------------------------------------------------------------- Популярные игры
        static Color FpsColor(int fps) { return fps < 0 ? Theme.Text3 : fps < 30 ? Theme.Red : fps < 60 ? Theme.Orange : Theme.Green; }

        static TableCell FpsCell(int fps, bool cpuBound, bool engineBound)
        {
            if (fps < 0) return TableCell.Of("нет данных", Theme.Text3);
            TableCell c = TableCell.Of((fps < 20 ? "< 20" : fps.ToString()) + (cpuBound ? " *" : engineBound ? " ⁿ" : ""), FpsColor(fps));
            c.Bold = true;
            return c;
        }

        void FillPopular()
        {
            popTable.ClearRows();
            foreach (PopularGame g in FpsData.Games)
            {
                TableCell name = new TableCell(g.Name); name.Bold = true;
                TableCell req;
                if (g.AppId == 0) req = TableCell.Pill("Нет в Steam", Theme.Text3);
                else if (!reqs.ContainsKey(g.AppId)) req = TableCell.Of("загружаю…", Theme.Text3);
                else req = ReqCell(reqs[g.AppId]);
                bool cb, eb, cb2, eb2;
                int lo = profile == null ? -1 : FpsData.Estimate(g, Preset.Low1080, profile.Gpu, profile.Cpu, out cb, out eb);
                int hi = profile == null ? -1 : FpsData.Estimate(g, Preset.High1080, profile.Gpu, profile.Cpu, out cb2, out eb2);
                if (profile == null) { cb = eb = cb2 = eb2 = false; }
                popTable.Rows.Add(new TableCell[] { name, TableCell.Of(g.Genre, Theme.Text2), req, FpsCell(lo, false, false), FpsCell(hi, false, false) });
            }
            popTable.Done();
        }

        TableCell ReqCell(GameReq r)
        {
            if (r.Error != null || !r.HasAny || profile == null) return TableCell.Pill("Нет данных", Theme.Text3);
            GameVerdict v = GameVerdict.Make(profile, r);
            return TableCell.Pill(v.Title, VerdictColor(v.Fit));
        }

        static Color VerdictColor(Fit f) { return f == Fit.Recommended ? Theme.Green : f == Fit.Minimum ? Theme.Orange : f == Fit.Below ? Theme.Red : Theme.Text3; }

        void LoadPopularReqs()
        {
            int gen = ++popGen;
            ThreadPool.QueueUserWorkItem(delegate
            {
                foreach (PopularGame g in FpsData.Games)
                {
                    if (gen != popGen) return;
                    if (g.AppId == 0) continue;
                    bool net; GameReq r;
                    try { r = SteamReq.Get(g.AppId, out net); }
                    catch (Exception ex) { r = new GameReq(); r.AppId = g.AppId; r.Error = ex.Message; net = false; }
                    GameReq res = r; int id = g.AppId;
                    try
                    {
                        BeginInvoke(new Action(delegate
                        {
                            if (gen != popGen) return;
                            reqs[id] = res;
                            FillPopular();
                            if (detail != null && detail.AppId == id) ShowDetail(detail);
                        }));
                    }
                    catch { return; }
                    if (net) Thread.Sleep(350);
                }
            });
        }

        // ---------------------------------------------------------------- Таблица FPS одной игры
        void ShowDetail(PopularGame g)
        {
            detail = g;
            detailCard.Clear();
            FlatButton back = new FlatButton("Все игры", false, Icons.Undo);
            back.Click += delegate { detail = null; ShowTab("games"); };
            bool cb, eb;
            int myLo = profile == null ? -1 : FpsData.Estimate(g, Preset.Low1080, profile.Gpu, profile.Cpu, out cb, out eb);
            int myHi = profile == null ? -1 : FpsData.Estimate(g, Preset.High1080, profile.Gpu, profile.Cpu, out cb, out eb);
            string mine = myLo < 0 ? "Для вашей видеокарты оценки нет: Tunex не знает эту модель."
                : "На этом ПК примерно " + myLo + " FPS на низких и " + myHi + " FPS на высоких настройках в Full HD.";
            SettingRow head = detailCard.Add(new SettingRow(g.Name, g.Genre + " · " + mine, back));
            head.Glyph = Icons.Game; head.GlyphColor = FpsColor(myHi);
            head.TitleFont = Theme.FontH2;

            // Требования Steam
            if (g.AppId == 0)
            {
                SettingRow r = detailCard.Add(new SettingRow("Требования Steam", "Этой игры нет в Steam — сравнить с требованиями нельзя.", null));
                r.Glyph = Icons.Info; r.GlyphColor = Theme.Text3;
                r.Badges.Add(new Badge("Нет данных", Theme.Text3));
            }
            else if (!reqs.ContainsKey(g.AppId))
            {
                SettingRow r = detailCard.Add(new SettingRow("Требования Steam", "Загружаю…", null));
                r.Glyph = Icons.Info; r.GlyphColor = Theme.Text3;
            }
            else
            {
                GameReq rq = reqs[g.AppId];
                SettingRow r;
                if (rq.Error != null || !rq.HasAny || profile == null)
                {
                    r = detailCard.Add(new SettingRow("Требования Steam", rq.Error != null ? rq.Error + "." : "Издатель не указал требования для Windows.", null));
                    r.Badges.Add(new Badge("Нет данных", Theme.Text3)); r.GlyphColor = Theme.Text3;
                }
                else
                {
                    GameVerdict v = GameVerdict.Make(profile, rq);
                    List<string> lines = new List<string>();
                    foreach (PartResult p in v.Parts) lines.Add(p.Part + " — " + p.Text);
                    r = detailCard.Add(new SettingRow("Требования Steam", string.Join("\n", lines.ToArray()), null));
                    r.Badges.Add(new Badge(v.Title, VerdictColor(v.Fit))); r.GlyphColor = VerdictColor(v.Fit);
                }
                r.Glyph = Icons.Info;
            }

            // Выбор процессора
            DarkDropdown cpu = new DarkDropdown();
            cpu.Width = Theme.S(280);
            cpu.Items.Add("Ваш: " + (profile != null && profile.Cpu > 0 ? profile.CpuName : "неизвестен"));
            foreach (string[] c in FpsData.TableCpus) cpu.Items.Add(c[1]);
            cpu.SelectedIndex = detailCpu;
            cpu.SelectedIndexChanged += delegate { detailCpu = cpu.SelectedIndex; FillFps(); };
            SettingRow cr = detailCard.Add(new SettingRow("Процессор для таблицы", "Сильная видеокарта со слабым процессором упирается в процессор — выберите, с каким считать.", cpu));
            cr.Glyph = Icons.Cpu; cr.GlyphColor = Theme.Text2;
            if (g.Note != null) detailCard.Footer = g.Note; else detailCard.Footer = null;
            detailCard.Relayout();

            FillFps();
            if (tab == "games") ShowTab("games");
        }

        void FillFps()
        {
            PopularGame g = detail;
            if (g == null) return;
            int cpu = detailCpu == 0 ? (profile != null ? profile.Cpu : -1) : FpsData.CpuScore(FpsData.TableCpus[detailCpu - 1][0]);
            string myName = profile != null && profile.Gpu > 0 ? profile.GpuName : null;
            Preset[] ps = { Preset.Low1080, Preset.High1080, Preset.High1440 };
            bool anyCpu = false, anyEngine = false;
            fpsTable.ClearRows();

            // Строка с FPS для одной видеокарты; lo/hi — разброс по группе
            Func<string, int, bool, int[], int[], TableCell[]> make = delegate(string label, int score, bool mine, int[] lo, int[] hi)
            {
                TableCell name = new TableCell(label);
                name.Bold = mine; name.Color = mine ? Theme.Accent : Theme.Text;
                TableCell[] row = new TableCell[5];
                row[0] = name;
                row[1] = TableCell.Of(score.ToString(), Theme.Text3);
                for (int i = 0; i < 3; i++)
                {
                    bool cb, eb;
                    int f = FpsData.Estimate(g, ps[i], score, cpu, out cb, out eb);
                    row[i + 2] = FpsCell(f, cb, eb);
                    anyCpu |= cb; anyEngine |= eb;
                    if (lo != null && f >= 0) { lo[i] = lo[i] < 0 ? f : Math.Min(lo[i], f); hi[i] = Math.Max(hi[i], f); }
                }
                return row;
            };

            // Ваша видеокарта — всегда первой строкой
            if (myName != null) fpsTable.Highlight = fpsTable.AddRow(make("Ваша: " + Short(myName), profile.Gpu, true, null, null), false);

            foreach (string[] grp in FpsData.GpuGroups)
            {
                List<KeyValuePair<string, int>> list = new List<KeyValuePair<string, int>>();
                for (int i = 1; i < grp.Length; i++) list.Add(new KeyValuePair<string, int>(grp[i], FpsData.GpuScore(grp[i])));
                list.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b) { return a.Value.CompareTo(b.Value); });
                int[] lo = { -1, -1, -1 }, hi = { 0, 0, 0 };
                List<TableCell[]> rows = new List<TableCell[]>();
                foreach (KeyValuePair<string, int> kv in list) rows.Add(make(kv.Key, kv.Value, false, lo, hi));
                TableCell[] head = new TableCell[5];
                head[0] = new TableCell(grp[0]); head[0].Bold = true;
                head[1] = TableCell.Of(list.Count + " шт.", Theme.Text3);
                for (int i = 0; i < 3; i++)
                    head[i + 2] = lo[i] < 0 ? TableCell.Of("—", Theme.Text3)
                        : TableCell.Of((lo[i] < 20 ? "< 20" : lo[i].ToString()) + " – " + hi[i], Theme.Text2);
                fpsTable.AddGroup(head);
                foreach (TableCell[] r in rows) fpsTable.AddRow(r, true);
            }
            string note = "Примерная оценка, а не замер: типичный FPS из обзоров для RTX 3060 и Core i5-12400, пересчитанный по силе видеокарты и процессора. " +
                "Реальный FPS может отличаться примерно на четверть — от карты, сцены, драйвера и обновлений игры. Красный — меньше 30 FPS, оранжевый — 30–60, зелёный — 60 и больше.";
            if (anyCpu) note += " * — упирается в процессор: с более сильным процессором будет больше.";
            if (anyEngine) note += " ⁿ — предел самой игры.";
            if (cpu < 0) note += " Ваш процессор Tunex не знает, поэтому таблица считается только по видеокарте.";
            fpsTable.Footer = note;
            fpsTable.Done();
        }

        static string Short(string gpu)
        {
            string s = gpu.Replace("(R)", "").Replace("(TM)", "").Replace("NVIDIA ", "").Replace("AMD ", "").Replace("Intel ", "Intel ");
            return System.Text.RegularExpressions.Regex.Replace(s, "\\s+", " ").Trim();
        }

        // ---------------------------------------------------------------- Свои игры Steam: установленные + добавленные вручную
        List<SteamGame> GameList()
        {
            List<SteamGame> list = new List<SteamGame>();
            try { list.AddRange(SteamLib.Installed()); } catch { }
            foreach (string e in App.Manager.State.ReqGames)
            {
                int bar = e.IndexOf('|'); int id;
                if (bar <= 0 || !int.TryParse(e.Substring(0, bar), out id)) continue;
                bool dup = false; foreach (SteamGame x in list) if (x.AppId == id) dup = true;
                if (dup) continue;
                SteamGame g = new SteamGame(); g.AppId = id; g.Name = e.Substring(bar + 1);
                list.Add(g);
            }
            return list;
        }

        void LoadGames()
        {
            int gen = ++gamesGen;
            gamesCard.Clear();
            List<SteamGame> games = GameList();
            gamesCard.Footer = "Здесь только сравнение с системными требованиями из магазина Steam. «Рекомендуемые» обычно значит высокие настройки в Full HD, «Минимальные» — низкие. " +
                "Если в требованиях не названа конкретная модель или игры нет в Steam, Tunex пишет «Нет данных», а не угадывает. В Steam отправляется только номер игры или текст поиска.";
            if (games.Count == 0)
            {
                SettingRow r = gamesCard.Add(new SettingRow(SteamLib.Present ? "В Steam нет установленных игр" : "Steam на этом ПК не найден",
                    "Найдите игру по названию в поле выше — Tunex сравнит её требования с этим ПК. Игры из Epic, Battle.net и других магазинов проверить нельзя: они не публикуют требования для программ.", null));
                r.Glyph = Icons.Game; r.GlyphColor = Theme.Text3;
                return;
            }
            List<KeyValuePair<SteamGame, SettingRow>> rows = new List<KeyValuePair<SteamGame, SettingRow>>();
            foreach (SteamGame g in games) rows.Add(new KeyValuePair<SteamGame, SettingRow>(g, AddGameRow(g)));
            ThreadPool.QueueUserWorkItem(delegate
            {
                foreach (KeyValuePair<SteamGame, SettingRow> kv in rows)
                {
                    if (gen != gamesGen) return;
                    bool net; GameReq r;
                    try { r = SteamReq.Get(kv.Key.AppId, out net); }
                    catch (Exception ex) { r = new GameReq(); r.AppId = kv.Key.AppId; r.Error = ex.Message; net = false; }
                    KeyValuePair<SteamGame, SettingRow> item = kv; GameReq res = r;
                    try { BeginInvoke(new Action(delegate { if (gen == gamesGen) ShowGame(item.Key, item.Value, res); })); } catch { return; }
                    if (net) Thread.Sleep(350);   // не чаще ~3 запросов в секунду: у Steam есть ограничение
                }
            });
        }

        SettingRow AddGameRow(SteamGame g)
        {
            Control acc = null;
            if (!g.Installed)
            {
                IconButton del = new IconButton(Icons.Delete, Theme.S(32));
                SteamGame gg = g;
                del.Click += delegate { RemoveGame(gg); };
                acc = del;
            }
            SettingRow row = gamesCard.Add(new SettingRow(g.Name, "Загружаю требования из Steam…", acc));
            row.Glyph = Icons.Game; row.GlyphColor = Theme.Text3;
            row.DescriptionColor = Theme.Text3;
            row.Badges.Add(new Badge(g.Installed ? "Установлена" : "Добавлена", Theme.Text3));
            return row;
        }

        void ShowGame(SteamGame g, SettingRow row, GameReq r)
        {
            row.Badges.Clear();
            if (r.Error == null && g.Installed && r.Type != null && r.Type != "game" && r.Type != "demo")
            {
                row.Hidden = true; row.Visible = false; gamesCard.Relayout(); return;   // программа, а не игра (например, Wallpaper Engine)
            }
            if (!string.IsNullOrEmpty(r.Name)) row.Title = r.Name;
            if (r.Error != null)
            {
                row.Badges.Add(new Badge("Нет данных", Theme.Text3));
                row.Description = r.Error + ".";
                row.DescriptionColor = Theme.Text3; row.GlyphColor = Theme.Text3;
            }
            else if (!r.HasAny)
            {
                row.Badges.Add(new Badge("Нет данных", Theme.Text3));
                row.Description = "Издатель не указал в Steam системные требования для Windows — сравнить не с чем.";
                row.DescriptionColor = Theme.Text3; row.GlyphColor = Theme.Text3;
            }
            else
            {
                GameVerdict v = GameVerdict.Make(profile, r);
                Color c = VerdictColor(v.Fit);
                row.Badges.Add(new Badge(v.Title, c));
                string head = v.Fit == Fit.Recommended ? "Ваш ПК не слабее рекомендуемого — можно играть на высоких настройках."
                    : v.Fit == Fit.Minimum ? "Запустится, но, скорее всего, на низких или средних настройках."
                    : v.Fit == Fit.Below ? "Может не запуститься или будет сильно тормозить."
                    : "Честно сравнить нельзя: в требованиях не названа конкретная видеокарта.";
                List<string> lines = new List<string>(); lines.Add(head);
                foreach (PartResult p in v.Parts) lines.Add(p.Part + " — " + p.Text);
                row.Description = string.Join("\n", lines.ToArray());
                row.DescriptionColor = Theme.Text2; row.GlyphColor = c;
            }
            row.Invalidate();
            gamesCard.Relayout();
        }

        void RemoveGame(SteamGame g)
        {
            App.Manager.State.ReqGames.RemoveAll(delegate(string e) { return e.StartsWith(g.AppId + "|"); });
            App.Manager.State.Save();
            LoadGames();
        }

        void Search()
        {
            string term = query.Text.Trim();
            if (term.Length < 2) { App.Toast("Введите хотя бы две буквы названия.", Theme.Orange); return; }
            find.Enabled = false;
            resultCard.Clear(); resultOpen = true; resultCard.Visible = true;
            SettingRow wait = resultCard.Add(new SettingRow("Ищу «" + term + "» в Steam…", null, null));
            wait.Glyph = Icons.Search; wait.GlyphColor = Theme.Text3;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<SteamGame> found = null; string err = null;
                try { found = SteamReq.Search(term); } catch (Exception ex) { err = ex is System.Net.WebException ? "Не удалось связаться со Steam — проверьте интернет." : ex.Message; }
                try { BeginInvoke(new Action(delegate { ShowSearch(term, found, err); })); } catch { }
            });
        }

        void CloseResults() { resultCard.Clear(); resultOpen = false; resultCard.Visible = false; }

        void ShowSearch(string term, List<SteamGame> found, string err)
        {
            find.Enabled = true;
            resultCard.Clear();
            IconButton close = new IconButton(Icons.Close, Theme.S(32));
            close.Click += delegate { CloseResults(); };
            if (err != null || found == null || found.Count == 0)
            {
                SettingRow r = resultCard.Add(new SettingRow(err != null ? "Поиск не удался" : "Ничего не нашлось",
                    err ?? "В Steam нет игры с названием «" + term + "». Попробуйте написать по-английски, как в магазине.", close));
                r.Glyph = Icons.Info; r.GlyphColor = Theme.Text3;
                return;
            }
            SettingRow head = resultCard.Add(new SettingRow("Найдено в Steam: " + found.Count, "Выберите игру — она появится в списке ниже.", close));
            head.Glyph = Icons.Search; head.GlyphColor = Theme.Text2;
            foreach (SteamGame g in found)
            {
                FlatButton add = new FlatButton("Проверить", true);
                SteamGame gg = g;
                add.Click += delegate
                {
                    string key = gg.AppId + "|";
                    if (!App.Manager.State.ReqGames.Exists(delegate(string e) { return e.StartsWith(key); }))
                    {
                        App.Manager.State.ReqGames.Add(key + gg.Name);
                        App.Manager.State.Save();
                    }
                    CloseResults(); query.Text = "";
                    LoadGames();
                };
                SettingRow r = resultCard.Add(new SettingRow(g.Name, null, add));
                r.Glyph = Icons.Game; r.GlyphColor = Theme.Text2;
            }
        }
    }

    // ---------------------------------------------------------------- Оформление
    class LookPage : Page
    {
        public LookPage()
        {
            Add(new PageHeader(Icons.Palette, "Оформление", null, null));
            Add(new SectionLabel("Цвет акцента"));
            Add(new AccentPicker());
            Card c = Add(new Card());
            SettingRow r = c.Add(new SettingRow("Цвет меняется сразу",
                "Им подсвечиваются кнопки, переключатели, активный раздел и графики. Цвета состояний настроек (красный, жёлтый, зелёный) не меняются, чтобы их было легко различать.", null));
            r.Glyph = Icons.Info; r.GlyphColor = Theme.Text2;
        }
    }

    // Плитки с цветами: три готовых и «Свой цвет»
    class AccentPicker : BufferedControl
    {
        int hover = -1;
        const int Count = 4;

        public AccentPicker() { Height = Theme.S(118); Cursor = Cursors.Hand; }

        Rectangle Tile(int i)
        {
            int gap = Theme.S(12), w = Math.Min(Theme.S(200), (Width - gap * (Count - 1)) / Count);
            return new Rectangle(i * (w + gap), 0, w, Height - 1);
        }

        int At(Point p) { for (int i = 0; i < Count; i++) if (Tile(i).Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = At(e.Location); if (h != hover) { hover = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }

        bool IsPreset(out int idx)
        {
            idx = Array.IndexOf(Theme.Presets, Theme.Accent);
            for (int i = 0; i < Theme.Presets.Length; i++) if (Theme.Presets[i].ToArgb() == Theme.Accent.ToArgb()) idx = i;
            return idx >= 0;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int i = At(e.Location);
            if (i < 0) return;
            if (i < 3) { App.Form.SetAccent(Theme.Presets[i]); return; }
            using (ColorDialog d = new ColorDialog())
            {
                d.FullOpen = true; d.Color = Theme.Accent; d.AnyColor = true;
                if (d.ShowDialog(FindForm()) == DialogResult.OK) App.Form.SetAccent(d.Color);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Theme.Smooth(g);
            int sel; bool preset = IsPreset(out sel);
            if (!preset) sel = 3;
            for (int i = 0; i < Count; i++)
            {
                Rectangle t = Tile(i);
                RectangleF rf = new RectangleF(t.X + 0.5f, t.Y + 0.5f, t.Width - 1, t.Height - 1);
                Theme.FillRound(g, i == hover ? Theme.CardHover : Theme.Card, rf, 2);
                Theme.DrawRound(g, i == sel ? Theme.Accent : Theme.Border, rf, 2, i == sel ? Theme.Sf(2) : 1f);
                int p = Theme.S(14);
                Rectangle sw = new Rectangle(t.X + p, t.Y + p, t.Width - p * 2, Theme.S(46));
                if (i < 3)
                    using (SolidBrush b = new SolidBrush(Theme.Presets[i])) g.FillRectangle(b, sw);
                else
                {
                    // Радуга для «своего цвета»; если выбран свой — он сам
                    if (!preset) using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, sw);
                    else
                        using (LinearGradientBrush lb = new LinearGradientBrush(sw, Color.Red, Color.Blue, 0f))
                        {
                            ColorBlend cb = new ColorBlend();
                            cb.Colors = new Color[] { Color.FromArgb(240, 96, 78), Color.FromArgb(240, 200, 64), Color.FromArgb(120, 220, 100), Color.FromArgb(77, 201, 220), Color.FromArgb(120, 120, 255), Color.FromArgb(220, 110, 220) };
                            cb.Positions = new float[] { 0f, 0.2f, 0.4f, 0.6f, 0.8f, 1f };
                            lb.InterpolationColors = cb;
                            g.FillRectangle(lb, sw);
                        }
                }
                string name = i < 3 ? Theme.PresetNames[i] : "Свой цвет";
                string sub = i < 3 ? Theme.ToHex(Theme.Presets[i]) : (preset ? "выбрать…" : Theme.ToHex(Theme.Accent));
                int ty = sw.Bottom + Theme.S(10);
                Theme.DrawText(g, name, Theme.FontBodyBold, Theme.Text, new Rectangle(sw.X, ty, sw.Width, Theme.S(22)), TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                Theme.DrawText(g, sub, Theme.FontLabel, Theme.Text3, new Rectangle(sw.X, ty, sw.Width, Theme.S(22)), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                if (i == sel)
                    Theme.Glyph(g, Icons.Check, Theme.FontIconSmall, Theme.Luma(i < 3 ? Theme.Presets[i] : Theme.Accent) > 0.55f ? Theme.OnAccentFor(i < 3 ? Theme.Presets[i] : Theme.Accent) : Color.White,
                        new Rectangle(sw.Right - Theme.S(26), sw.Y, Theme.S(26), Theme.S(26)));
            }
        }
    }

    // ---------------------------------------------------------------- Бэкапы
    class BackupPage : Page
    {
        readonly Card origCard, listCard;
        readonly FlatButton make;
        int gen;

        public BackupPage()
        {
            make = new FlatButton("Сделать бэкап сейчас", true, Icons.Save);
            make.Click += delegate { MakeNow(); };
            Add(new PageHeader(Icons.Save, "Бэкапы", "Бэкап запоминает сразу все настройки Tunex. Вернуться к любому бэкапу можно одной кнопкой.", make));
            Add(new SectionLabel("Исходное состояние"));
            origCard = Add(new Card());
            Add(new SectionLabel("Сохранённые бэкапы"));
            listCard = Add(new Card());
        }

        public override void OnShow() { Reload(); }

        void Reload()
        {
            int g = ++gen;
            origCard.Clear(); listCard.Clear();
            SettingRow w = origCard.Add(new SettingRow("Сравниваю бэкапы с текущими настройками…", null, null));
            w.Glyph = Icons.Refresh; w.GlyphColor = Theme.Text3;
            ThreadPool.QueueUserWorkItem(delegate
            {
                BackupFile orig = null; List<BackupFile> list = new List<BackupFile>(); Dictionary<string, string> cur = new Dictionary<string, string>();
                try { Backups.EnsureOriginal(); orig = Backups.Original(); list = Backups.List(); cur = Backups.Current(); } catch { }
                try { BeginInvoke(new Action(delegate { if (g == gen) Fill(orig, list, cur); })); } catch { }
            });
        }

        void Fill(BackupFile orig, List<BackupFile> list, Dictionary<string, string> cur)
        {
            SuspendLayout();
            origCard.Clear(); listCard.Clear();

            if (orig != null)
            {
                List<BackupItem> d = Backups.Diff(orig, cur);
                FlatButton b = new FlatButton("Восстановить", false, Icons.Undo);
                b.Enabled = d.Count > 0;
                b.Click += delegate { AskRestore(orig, d); };
                SettingRow r = origCard.Add(new SettingRow("Как было до Tunex",
                    "Запомнено " + Backups.Short(orig.When) + ". " + (d.Count == 0 ? "Сейчас всё так же — восстанавливать нечего." : "С тех пор изменено настроек: " + d.Count + "."), b));
                r.Glyph = Icons.Home; r.GlyphColor = Theme.Accent;
                r.Badges.Add(new Badge("Не удаляется", Theme.Text3));
            }

            if (list.Count == 0)
            {
                SettingRow r = listCard.Add(new SettingRow("Пока нет бэкапов",
                    "Нажмите «Сделать бэкап сейчас». Ещё Tunex делает бэкап сам — раз в день перед первым изменением.", null));
                r.Glyph = Icons.Save; r.GlyphColor = Theme.Text3;
            }
            for (int i = 0; i < list.Count; i++)
            {
                BackupFile bf = list[i];
                List<BackupItem> d = Backups.Diff(bf, cur);
                FlatButton restore = new FlatButton("Восстановить", false, Icons.Undo);
                restore.Enabled = d.Count > 0;
                IconButton del = new IconButton(Icons.Delete, Theme.S(34));
                restore.Click += delegate { AskRestore(bf, d); };
                del.Click += delegate
                {
                    if (!DarkDialog.Ask(App.Form, "Удалить бэкап?", "Бэкап от " + Backups.Short(bf.When) + " будет удалён. Настройки ПК это не изменит.", "Удалить", "Отмена")) return;
                    Backups.Delete(bf); App.Form.UpdateBackupButton(); Reload();
                };
                Panel holder = new Panel();
                holder.BackColor = Color.Transparent;
                holder.Size = new Size(restore.Width + Theme.S(8) + del.Width, Math.Max(restore.Height, del.Height));
                restore.Location = new Point(0, (holder.Height - restore.Height) / 2);
                del.Location = new Point(restore.Width + Theme.S(8), (holder.Height - del.Height) / 2);
                holder.Controls.Add(restore); holder.Controls.Add(del);

                string desc = "Включено настроек: " + bf.OnCount + " из " + bf.Items.Count + " · " +
                    (d.Count == 0 ? "совпадает с тем, что сейчас" : "отличается от текущих: " + d.Count);
                SettingRow r = listCard.Add(new SettingRow(Cap(Backups.Short(bf.When)), desc, holder));
                r.Glyph = Icons.Save; r.GlyphColor = bf.Kind == "manual" ? Theme.Accent : Theme.Text2;
                r.Badges.Add(new Badge(Backups.KindName(bf), bf.Kind == "manual" ? Theme.Accent : Theme.Text3));
                if (i == 0) r.Badges.Add(new Badge("Последний", Theme.Green));
            }
            listCard.Footer = "Хранятся последние " + Backups.Keep + " бэкапов. Сам Tunex делает бэкап раз в день перед первым изменением и каждый раз перед восстановлением — " +
                "так что любое восстановление тоже можно отменить. Яркость, гамма и частота экрана в бэкап не входят. Отменить одно отдельное изменение можно в «Журнале».";
            ResumeLayout();
            PerformLayout();
        }

        static string Cap(string s) { return s.Length == 0 ? s : char.ToUpper(s[0]) + s.Substring(1); }

        void MakeNow()
        {
            make.Enabled = false;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err = null;
                try { Backups.Create("manual", "Вручную"); } catch (Exception ex) { err = ex.Message; }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        make.Enabled = true;
                        if (err != null) App.Error("Бэкап не сохранён: " + err);
                        else App.Toast("Бэкап сохранён", Theme.Green);
                        App.Form.UpdateBackupButton();
                        Reload();
                    }));
                }
                catch { }
            });
        }

        void AskRestore(BackupFile b, List<BackupItem> diff)
        {
            if (diff.Count == 0) { App.Toast("Настройки и так совпадают с этим бэкапом.", Theme.Green); return; }
            StringBuilder sb = new StringBuilder();
            sb.Append("Изменятся настройки (" + diff.Count + "):\n");
            bool reboot = false; List<string> restart = new List<string>();
            for (int i = 0; i < diff.Count; i++)
            {
                Tweak t = Catalog.Resolve(diff[i].Id);
                if (t != null && t.NeedsReboot) reboot = true;
                if (t != null && t.ConfirmText != null) restart.Add(t.Title);
                if (i < 8) sb.Append("• " + diff[i].Title + (diff[i].Id == Catalog.PowerPlan.Id ? " — вернётся прежняя схема" : diff[i].On ? " — включится" : " — выключится") + "\n");
            }
            if (diff.Count > 8) sb.Append("…и ещё " + (diff.Count - 8) + "\n");
            if (reboot) sb.Append("\nЧасть настроек заработает после перезагрузки.");
            if (restart.Count > 0) sb.Append("\nУчтите: " + string.Join(", ", restart.ToArray()) + " — при этом перезапустится программа или сетевая карта.");
            sb.Append("\nПеред восстановлением Tunex сам сделает бэкап текущих настроек.");
            string title = b.Kind == "original" ? "Вернуть как было до Tunex?" : "Восстановить бэкап от " + Backups.Short(b.When) + "?";
            if (!DarkDialog.Ask(App.Form, title, sb.ToString(), "Восстановить", "Отмена")) return;

            App.Toast("Восстанавливаю…", Theme.Accent);
            ThreadPool.QueueUserWorkItem(delegate
            {
                JournalEntry e = null; bool rb = false; List<string> errors;
                try { errors = Backups.Restore(b, out e, out rb); }
                catch (Exception ex) { errors = new List<string>(); errors.Add(ex.Message); }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        int n = e != null ? e.Items.Count : 0;
                        if (e != null && n > 0) App.Manager.CommitFromUi(e);
                        App.ShowErrors(errors, rb ? "Восстановлено настроек: " + n + ". Часть заработает после перезагрузки." : "Восстановлено настроек: " + n + ".");
                        App.Form.UpdateBackupButton();
                        Reload();
                    }));
                }
                catch { }
            });
        }
    }

    // ---------------------------------------------------------------- Журнал: строка одного изменения
    class JournalRow : RowBase
    {
        readonly JournalEntry e;
        readonly Control accessory;
        readonly string kind, glyph, title, observe;
        readonly Color color;
        readonly List<KeyValuePair<string, Color>> chips = new List<KeyValuePair<string, Color>>();

        public JournalRow(JournalEntry entry, Control right, string observeText)
        {
            e = entry; accessory = right; observe = observeText;
            if (right != null) Controls.Add(right);
            string t = e.Title ?? "";
            bool single = e.Items.Count == 1;
            string id = e.Items.Count > 0 ? e.Items[0].TweakId : "";
            if (t.StartsWith("Восстановлен")) { kind = "Восстановление"; color = Theme.Accent; glyph = Icons.Save; title = t; }
            else if (single && id == "power.plan") { kind = "Схема питания"; color = Theme.Accent; glyph = Icons.Power; title = t; }
            else if (single && id.StartsWith("display.hz")) { kind = "Экран"; color = Theme.Accent; glyph = Icons.Display; title = t; }
            else if (single && e.Items[0].On) { kind = "Включено"; color = Theme.Green; glyph = Icons.Check; title = Strip(t, e.Items[0].Title); }
            else if (single) { kind = "Выключено"; color = Theme.Orange; glyph = Icons.Close; title = Strip(t, e.Items[0].Title); }
            else { kind = "Несколько настроек"; color = Theme.Accent; glyph = Icons.Settings; title = t; }
            if (e.Undone) { color = Theme.Text3; }
            if (!single || t.StartsWith("Восстановлен"))
                foreach (JournalItem it in e.Items)
                    chips.Add(new KeyValuePair<string, Color>((it.On ? "✓ " : "✕ ") + it.Title, e.Undone ? Theme.Text3 : it.On ? Theme.Green : Theme.Orange));
        }

        static string Strip(string t, string fallback)
        {
            if (t.StartsWith("Включено: ")) return t.Substring(10);
            if (t.StartsWith("Выключено: ")) return t.Substring(11);
            return string.IsNullOrEmpty(t) ? fallback : t;
        }

        static int Pad { get { return Theme.S(18); } }
        static int TimeW { get { return Theme.S(50); } }
        static int Tile { get { return Theme.S(34); } }
        int TextX { get { return Pad + TimeW + Tile + Theme.S(14); } }
        int TextW(int w) { return Math.Max(Theme.S(120), w - TextX - Pad - (accessory != null ? accessory.Width + Pad : 0)); }
        static int ChipH { get { return Theme.Measure("Ag", Theme.FontSmallBold).Height + Theme.S(6); } }

        // Раскладка плашек по строкам: возвращает высоту
        int LayoutChips(int w, List<Rectangle> rects)
        {
            int x = 0, y = 0;
            foreach (KeyValuePair<string, Color> c in chips)
            {
                int cw = Math.Min(w, Theme.Measure(c.Key, Theme.FontSmallBold).Width + Theme.S(16));
                if (x > 0 && x + cw > w) { x = 0; y += ChipH + Theme.S(6); }
                if (rects != null) rects.Add(new Rectangle(x, y, cw, ChipH));
                x += cw + Theme.S(6);
            }
            return chips.Count == 0 ? 0 : y + ChipH;
        }

        int ContentH(int w)
        {
            int tw = TextW(w);
            int h = Theme.Measure(title, Theme.FontCellTitle, tw).Height;
            if (chips.Count > 0) h += Theme.S(8) + LayoutChips(tw, null);
            if (observe != null) h += Theme.S(6) + Theme.Measure(observe, Theme.FontSmall, tw).Height;
            return h;
        }

        public override int MeasureHeight(int width) { return Math.Max(Theme.S(64), ContentH(width) + Pad * 2); }

        protected override void OnLayout(LayoutEventArgs ev)
        {
            base.OnLayout(ev);
            if (accessory != null) accessory.Location = new Point(Width - Pad - accessory.Width, (Height - accessory.Height) / 2);
        }

        protected override void OnPaint(PaintEventArgs ev)
        {
            Graphics g = ev.Graphics; Theme.Smooth(g);
            int tw = TextW(Width);
            int y = (Height - ContentH(Width)) / 2;
            int th = Theme.Measure(title, Theme.FontCellTitle, tw).Height;

            // Время
            Theme.DrawText(g, e.When.ToString("HH:mm"), Theme.FontBodyBold, Theme.Text2, new Rectangle(Pad, y, TimeW, th), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            // Значок вида изменения
            Rectangle tile = new Rectangle(Pad + TimeW, y + (th - Tile) / 2, Tile, Tile);
            Theme.FillRound(g, Theme.Alpha(color, 36), tile, Theme.Sf(4));
            Theme.Glyph(g, glyph, Theme.FontIconSmall, color, tile);

            // Заголовок и метка вида
            Theme.DrawText(g, title, Theme.FontCellTitle, e.Undone ? Theme.Text3 : Theme.Text, new Rectangle(TextX, y, tw, th), TextFormatFlags.WordBreak);
            string badge = e.Undone ? "Отменено" : kind;
            Size bs = Theme.Measure(badge, Theme.FontSmallBold);
            int bx = TextX + Math.Min(tw, Theme.Measure(title, Theme.FontCellTitle).Width) + Theme.S(10);
            int bw = bs.Width + Theme.S(14), bh = bs.Height + Theme.S(4);
            if (bx + bw <= TextX + tw)
            {
                Rectangle br = new Rectangle(bx, y + (Theme.Measure("Ag", Theme.FontCellTitle).Height - bh) / 2, bw, bh);
                Theme.FillRound(g, Theme.Alpha(color, 36), br, Theme.Sf(3));
                Theme.DrawText(g, badge, Theme.FontSmallBold, color, br, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                if (e.NeedsReboot && !e.Undone)
                {
                    string rb = "Нужна перезагрузка";
                    int rw = Theme.Measure(rb, Theme.FontSmallBold).Width + Theme.S(14);
                    Rectangle rr = new Rectangle(br.Right + Theme.S(6), br.Y, rw, bh);
                    if (rr.Right <= TextX + tw)
                    {
                        Theme.FillRound(g, Theme.Alpha(Theme.Purple, 36), rr, Theme.Sf(3));
                        Theme.DrawText(g, rb, Theme.FontSmallBold, Theme.Purple, rr, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    }
                }
            }
            y += th;

            // Что именно изменено — плашки
            if (chips.Count > 0)
            {
                y += Theme.S(8);
                List<Rectangle> rects = new List<Rectangle>();
                int ch = LayoutChips(tw, rects);
                for (int i = 0; i < chips.Count; i++)
                {
                    Rectangle r = rects[i]; r.Offset(TextX, y);
                    Theme.FillRound(g, Theme.Alpha(chips[i].Value, 26), r, Theme.Sf(3));
                    Theme.DrawText(g, chips[i].Key, Theme.FontSmallBold, chips[i].Value, r, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
                y += ch;
            }

            // Наблюдение: минуту до → минуту после
            if (observe != null)
            {
                y += Theme.S(6);
                int oh = Theme.Measure(observe, Theme.FontSmall, tw).Height;
                Theme.DrawText(g, observe, Theme.FontSmall, Theme.Text3, new Rectangle(TextX, y, tw, oh), TextFormatFlags.WordBreak);
            }
        }
    }

    // ---------------------------------------------------------------- Журнал
    class JournalPage : Page
    {
        public JournalPage()
        {
            App.Manager.JournalChanged += delegate { if (Visible) Rebuild(); };
        }

        public override void OnShow() { Rebuild(); }

        void Rebuild()
        {
            int scroll = -AutoScrollPosition.Y;
            SuspendLayout();
            Clear();
            FlatButton bk = new FlatButton("Бэкапы", false, Icons.Save);
            bk.Click += delegate { App.Form.Navigate("backup"); };
            Add(new PageHeader(Icons.History, "Журнал", "Все изменения по порядку — любое можно отменить. Вернуть всё сразу на нужный момент — в «Бэкапах».", bk));

            List<JournalEntry> list = App.Manager.State.Journal;
            if (list.Count == 0)
            {
                Card c = Add(new Card());
                SettingRow r = c.Add(new SettingRow("Пока пусто", "Здесь появятся изменения, которые вы примените.", null));
                r.Glyph = Icons.History; r.GlyphColor = Theme.Text3;
            }
            DateTime day = DateTime.MinValue;
            Card dayCard = null;
            foreach (JournalEntry e in list)
            {
                DateTime when = e.When;
                if (when.Date != day)
                {
                    day = when.Date;
                    string label = day == DateTime.Today ? "Сегодня" : day == DateTime.Today.AddDays(-1) ? "Вчера" : day.ToString("d MMMM yyyy", new CultureInfo("ru-RU"));
                    Add(new SectionLabel(label));
                    dayCard = Add(new Card());
                }
                AddEntry(dayCard, e);
            }
            ResumeLayout();
            FitWidths();
            AutoScrollPosition = new Point(0, scroll);
        }

        void AddEntry(Card c, JournalEntry e)
        {
            Control right = null;
            if (!e.Undone)
            {
                FlatButton undo = new FlatButton("Отменить", false, Icons.Undo);
                JournalEntry ee = e;
                undo.Click += delegate
                {
                    List<string> errors = App.Manager.Undo(ee);
                    App.ShowErrors(errors, ee.NeedsReboot ? "Отменено. Часть настроек вернётся после перезагрузки." : "Отменено");
                };
                right = undo;
            }
            string observe = null;
            if (e.After == null && !e.Undone)
            {
                int left = Math.Max(0, TweakManager.ObserveSeconds - (int)(DateTime.Now - e.When).TotalSeconds);
                if (left > 0) observe = "Слежу за системой… результат через " + left + " с.";
            }
            else if (e.After != null && e.Before != null && !e.Undone)
            {
                string cmp = App.Compare(e.Before, e.After);
                if (!string.IsNullOrEmpty(cmp)) observe = "Минуту до → минуту после: " + cmp;
            }
            c.Add(new JournalRow(e, right, observe));
        }

        int lastObs = -1;
        public override void OnTick()
        {
            JournalEntry o = App.Manager.Observing;
            int left = o == null ? -1 : (TweakManager.ObserveSeconds - (int)(DateTime.Now - o.When).TotalSeconds) / 5;
            if (o != null && left != lastObs) { lastObs = left; Rebuild(); }
        }
    }

    // ---------------------------------------------------------------- Настройки
    class SettingsPage : Page
    {
        readonly ToggleSwitch auto;
        readonly SettingRow verRow;
        readonly FlatButton check;

        public SettingsPage()
        {
            Add(new PageHeader(Icons.Settings, "Настройки", null, null));
            Add(new SectionLabel("Обновления"));
            Card c = Add(new Card());

            auto = new ToggleSwitch();
            auto.Checked = !App.Manager.State.NoAutoUpdate;
            auto.CheckedChanged += delegate
            {
                App.Manager.State.NoAutoUpdate = !auto.Checked;
                App.Manager.State.Save();
                App.Toast(auto.Checked ? "Автообновление включено" : "Автообновление выключено", Theme.Green);
            };
            SettingRow ar = c.Add(new SettingRow("Автообновление",
                "При запуске Tunex проверяет новую версию на GitHub и сам её скачивает. Новая версия откроется при следующем запуске — настройки, журнал и бэкапы сохранятся.", auto));
            ar.Glyph = Icons.Refresh; ar.GlyphColor = Theme.Accent;

            check = new FlatButton("Проверить сейчас", false, Icons.Refresh);
            check.Click += delegate { CheckNow(); };
            verRow = c.Add(new SettingRow("Версия " + Theme.Version, null, check));
            verRow.Glyph = Icons.Info; verRow.GlyphColor = Theme.Text2;
            c.Footer = Updater.Configured ? "Обновления берутся из выпусков проекта: " + Updater.ReleasesPage
                : "Адрес проекта на GitHub ещё не задан — обновления пока не работают.";
            check.Enabled = Updater.Configured;
            ShowStatus();
        }

        void ShowStatus()
        {
            AppState s = App.Manager.State;
            DateTime t;
            if (s.UpdateChecked != null && DateTime.TryParse(s.UpdateChecked, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t))
                verRow.Description = "Последняя проверка: " + Backups.Short(t) + " — " + s.UpdateStatus + ".";
            else verRow.Description = "Проверок ещё не было.";
            verRow.Invalidate();
            Card cc = verRow.Parent as Card;
            if (cc != null) cc.Relayout();
        }

        void CheckNow()
        {
            check.Enabled = false;
            verRow.Description = "Проверяю GitHub…"; verRow.Invalidate();
            AppState s = App.Manager.State;
            ThreadPool.QueueUserWorkItem(delegate
            {
                ReleaseInfo r; bool installed;
                Updater.CheckAndInstall(s, out r, out installed);
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        s.Save();
                        check.Enabled = true;
                        ShowStatus();
                        if (installed) App.Toast("Скачана версия " + r.Version.ToString(2) + ". Перезапустите Tunex, чтобы открыть её.", Theme.Green);
                    }));
                }
                catch { }
            });
        }

        public override void OnShow() { ShowStatus(); }
    }
}
