using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
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
            SectionHeading h = Add(new SectionHeading(glyph, title, subtitle));
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
            Add(new PageHeader(Icons.Chip, "NVIDIA", "Настройки драйвера из Панели управления NVIDIA — без поиска по меню.", null));
        }

        bool built;
        public override void OnShow()
        {
            if (built || !SysInfo.HardwareLoaded) return;
            built = true;
            Card c = Add(new Card());
            if (!SysInfo.HasNvidia)
            {
                SettingRow r = c.Add(new SettingRow("Видеокарта NVIDIA не найдена",
                    "На этом ПК: " + (SysInfo.Gpus.Count > 0 ? string.Join(", ", SysInfo.Gpus.ToArray()) : "—") + ". Раздел заработает на компьютере с NVIDIA.", null));
                r.Glyph = Icons.Info; r.GlyphColor = Theme.Text2;
                return;
            }
            FlatButton open = new FlatButton("Открыть", false);
            open.Click += delegate
            {
                try { Process.Start("explorer.exe", @"shell:AppsFolder\NVIDIACorp.NVIDIAControlPanel_56jybvy8sckqj!NVIDIACorp.NVIDIAControlPanel"); }
                catch (Exception ex) { App.Error(ex.Message); }
            };
            SettingRow head = c.Add(new SettingRow("Панель управления NVIDIA", string.Join(", ", SysInfo.Gpus.ToArray()), open));
            head.Glyph = Icons.Chip; head.GlyphColor = Theme.Green;

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

        public override void OnTick() { if (!built && SysInfo.HardwareLoaded) OnShow(); }
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
            FlatButton rp = new FlatButton("Точка восстановления", false, Icons.Shield);
            rp.Enabled = SysInfo.IsAdmin;
            rp.Click += delegate { CreateRestorePoint(rp); };
            Add(new PageHeader(Icons.History, "Журнал", "Каждое изменение запоминается: его можно откатить, а через минуту видно, как изменились загрузка и температуры.", rp));

            Card all = Add(new Card());
            FlatButton reset = new FlatButton("Вернуть всё", false, Icons.Undo);
            reset.Enabled = App.Manager.State.Originals.Count > 0;
            reset.Click += delegate
            {
                if (!DarkDialog.Ask(App.Form, "Вернуть всё как было?", "Все настройки, которые меняла программа (" + App.Manager.State.Originals.Count + "), вернутся к состоянию до её первого использования.", "Вернуть", "Отмена")) return;
                bool reboot;
                List<string> errors = App.Manager.RestoreAllOriginals(out reboot);
                App.ShowErrors(errors, reboot ? "Готово. Часть настроек вернётся после перезагрузки." : "Готово. Всё как было.");
                Rebuild();
            };
            SettingRow ar = all.Add(new SettingRow("Вернуть всё как было до программы",
                "Изменено настроек: " + App.Manager.State.Originals.Count + ". Исходные значения хранятся на этом ПК.", reset));
            ar.Glyph = Icons.Undo; ar.GlyphColor = Theme.Accent;

            List<JournalEntry> list = App.Manager.State.Journal;
            if (list.Count == 0)
            {
                Card c = Add(new Card());
                c.Add(new SettingRow("Пока пусто", "Здесь появятся изменения, которые вы примените.", null));
            }
            DateTime day = DateTime.MinValue;
            foreach (JournalEntry e in list)
            {
                DateTime when = e.When;
                if (when.Date != day)
                {
                    day = when.Date;
                    string label = day == DateTime.Today ? "Сегодня" : day == DateTime.Today.AddDays(-1) ? "Вчера" : day.ToString("d MMMM yyyy", new CultureInfo("ru-RU"));
                    Add(new SectionLabel(label));
                }
                AddEntry(e);
            }
            ResumeLayout();
            FitWidths();
            AutoScrollPosition = new Point(0, scroll);
        }

        void AddEntry(JournalEntry e)
        {
            Card c = Add(new Card());
            Control right;
            if (e.Undone) right = new Pill("Отменено", Theme.Text2);
            else
            {
                FlatButton undo = new FlatButton("Откатить", false, Icons.Undo);
                JournalEntry ee = e;
                undo.Click += delegate
                {
                    List<string> errors = App.Manager.Undo(ee);
                    App.ShowErrors(errors, ee.NeedsReboot ? "Откатено. Часть настроек вернётся после перезагрузки." : "Откатено");
                };
                right = undo;
            }
            SettingRow head = c.Add(new SettingRow(e.When.ToString("HH:mm") + "   " + e.Title, null, right));
            head.TitleFont = Theme.FontBodyBold;
            if (e.NeedsReboot && !e.Undone) head.Badges.Add(new Badge("Нужна перезагрузка", Theme.Purple));

            if (e.Items.Count > 1 || e.Items[0].Title != e.Title)
            {
                List<string> lines = new List<string>();
                foreach (JournalItem it in e.Items)
                    lines.Add((it.TweakId == "power.plan" || it.TweakId.StartsWith("display.hz") ? "• " : (it.On ? "✓ " : "✕ ")) + it.Title);
                c.Add(new SettingRow("Что изменено", string.Join("\n", lines.ToArray()), null));
            }

            SettingRow obs;
            if (e.After == null && !e.Undone)
            {
                int left = Math.Max(0, TweakManager.ObserveSeconds - (int)(DateTime.Now - e.When).TotalSeconds);
                obs = c.Add(new SettingRow("Идёт наблюдение…", "Результат будет через " + left + " с.", null));
                obs.GlyphColor = Theme.Orange;
            }
            else if (e.After != null && e.Before != null)
            {
                obs = c.Add(new SettingRow("Минуту до → минуту после", App.Compare(e.Before, e.After), null));
                obs.GlyphColor = Theme.Green;
            }
            else return;
            obs.Glyph = Icons.Eye;
        }

        void CreateRestorePoint(FlatButton b)
        {
            b.Enabled = false;
            App.Toast("Создаю точку восстановления… Это может занять до минуты.");
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err = Maintenance.CreateRestorePoint();
                App.Form.BeginInvoke(new Action(delegate
                {
                    b.Enabled = true;
                    if (err != null) App.Error("Точка восстановления не создана: " + err);
                    else App.Toast("Точка восстановления создана", Theme.Green);
                }));
            });
        }

        int lastObs = -1;
        public override void OnTick()
        {
            JournalEntry o = App.Manager.Observing;
            int left = o == null ? -1 : (TweakManager.ObserveSeconds - (int)(DateTime.Now - o.When).TotalSeconds) / 5;
            if (o != null && left != lastObs) { lastObs = left; Rebuild(); }
        }
    }
}
