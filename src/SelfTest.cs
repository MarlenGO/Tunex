using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Tunex
{
    // Проверка движка: применить → сохранить → откатить → «вернуть всё». Запуск: Tunex.exe --selftest отчёт.txt
    // Состояние пишется во временную папку (переменная TUNEX_STATE_DIR), настоящий журнал не трогается.
    static class SelfTest
    {
        public static void Run(string reportPath)
        {
            StringBuilder log = new StringBuilder();
            int fails = 0;
            Action<bool, string> check = delegate(bool ok, string what) { log.AppendLine((ok ? "OK    " : "FAIL  ") + what); if (!ok) fails++; };
            try
            {
                SensorService sensors = new SensorService();
                TweakManager m = new TweakManager(new AppState(), sensors);
                App.Manager = m;

                // 1. Обычная настройка реестра
                Tweak t = Catalog.Resolve("system.transparency");
                bool before = t.ReadEnabled();
                string rawBefore = Convert.ToString(Reg.Get("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency"));
                m.SetPending(t, !before);
                check(m.PendingCount == 1, "изменение попало в список несохранённых");
                JournalEntry e;
                List<string> errors = m.ApplyPending(out e);
                check(errors.Count == 0, "применено без ошибок " + string.Join("; ", errors.ToArray()));
                check(t.ReadEnabled() == !before, "состояние переключилось");
                check(m.State.Journal.Count == 1 && m.State.Originals.ContainsKey(t.Id), "запись в журнале и исходное значение сохранены");

                AppState reloaded = AppState.Load();
                check(reloaded.Journal.Count == 1 && reloaded.Journal[0].Items[0].TweakId == t.Id, "журнал сохранился на диск и читается обратно");

                m.Undo(e);
                string rawAfter = Convert.ToString(Reg.Get("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency"));
                check(t.ReadEnabled() == before && rawAfter == rawBefore, "откат вернул точное значение («" + rawBefore + "»)");

                // 2. «Вернуть всё» после двух изменений
                m.ApplyToggleNow(t, !before);
                Tweak mouse = Catalog.Resolve("game.mouse");
                string mouseSnap = mouse.Capture();
                bool reboot;
                m.RestoreAllOriginals(out reboot);
                check(t.ReadEnabled() == before, "«вернуть всё» восстановило исходное значение");
                check(mouse.Capture() == mouseSnap, "мышь не тронута");

                // Бэкапы: снимок → изменение → видно отличие → восстановление → снова совпадает, а восстановление попало в журнал
                {
                    System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                    BackupFile bk = Backups.Create("manual", "Вручную");
                    long ms = sw.ElapsedMilliseconds;
                    check(bk.Items.Count > 10 && File.Exists(bk.Path), "бэкап сохранён: настроек " + bk.Items.Count + ", включено " + bk.OnCount + ", за " + ms + " мс");
                    bool tb = t.ReadEnabled();
                    m.ApplyToggleNow(t, !tb);
                    List<BackupItem> diff = Backups.Diff(bk, Backups.Current());
                    check(diff.Count == 1 && diff[0].Id == t.Id, "после изменения бэкап отличается на 1 настройку: " + diff.Count);
                    JournalEntry re; bool rb;
                    List<string> rerr = Backups.Restore(bk, out re, out rb);
                    m.CommitFromUi(re);
                    check(rerr.Count == 0 && t.ReadEnabled() == tb && Backups.Diff(bk, Backups.Current()).Count == 0, "восстановление вернуло настройку " + string.Join("; ", rerr.ToArray()));
                    check(m.State.Journal[0].Title.StartsWith("Восстановлен бэкап"), "восстановление записано в журнал: «" + m.State.Journal[0].Title + "»");
                    check(Backups.List().Count >= 2, "перед восстановлением сделан бэкап «Перед восстановлением»: всего " + Backups.List().Count);
                    Backups.EnsureOriginal();
                    check(Backups.Original() != null, "«Как было до Tunex» запомнено");
                }

                // Замер FPS: подсчёт на искусственных кадрах (ровные 100 FPS и три рывка по 80 мс)
                {
                    List<float> ms = new List<float>();
                    for (int i = 0; i < 6000; i++) ms.Add(i % 2000 == 1000 ? 80f : 10f);
                    FpsRun fr = FpsRun.Compute("test", ms.ToArray());
                    check(fr != null && Math.Abs(fr.Avg - 99.65f) < 0.1f && fr.Stutters == 3 && Math.Abs(fr.Low1 - 100) < 0.5f && fr.Graph.Length >= 50,
                        "замер FPS считается: средний " + (fr != null ? fr.Avg.ToString("0.0") : "—") + ", рывков " + (fr != null ? fr.Stutters : -1));
                    check(FpsCapture.IsNotGame("C:\\x\\msedge.exe") && FpsCapture.IsNotGame("Tunex.exe") && !FpsCapture.IsNotGame("cs2.exe"), "браузеры и сам Tunex не считаются игрой");
                    log.AppendLine("      PresentMon скачан: " + PresentMon.Installed);
                }

                // Автообновление: номера версий, ответ GitHub, подмена файла (на копии во временной папке, без интернета)
                {
                    check(Updater.Parse("v0.2") > Updater.Parse("0.1") && Updater.Parse("v1.0.1") > Updater.Parse("1.0") && Updater.Parse("0.1") == Updater.Parse("v0.1.0") && Updater.Parse("beta") == null,
                        "номера версий сравниваются: v0.2 > 0.1, v1.0.1 > 1.0");
                    string dir = Path.Combine(Path.GetTempPath(), "tunex-update-test");
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);
                    Directory.CreateDirectory(dir);
                    string exe = Path.Combine(dir, "Tunex.exe"), src = Path.Combine(dir, "release.bin");
                    File.WriteAllText(exe, "MZ old version");
                    File.WriteAllText(src, "MZ new version 0.2");
                    string json = "{\"tag_name\":\"v0.2\",\"html_url\":\"https://github.com/x/y/releases/tag/v0.2\",\"body\":\"Что нового\",\"assets\":[{\"name\":\"notes.txt\",\"size\":5,\"browser_download_url\":\"x\"},{\"name\":\"Tunex.exe\",\"size\":"
                        + new FileInfo(src).Length + ",\"browser_download_url\":\"" + new Uri(src).AbsoluteUri + "\"}]}";
                    ReleaseInfo ri = Updater.ParseRelease(json);
                    check(ri != null && ri.Version == new Version(0, 2, 0) && ri.AssetSize == new FileInfo(src).Length && ri.AssetUrl.StartsWith("file:"), "ответ GitHub разобран: " + (ri == null ? "null" : ri.Tag + ", файл " + ri.AssetSize + " байт"));
                    string ierr = Updater.Install(ri, exe);
                    check(ierr == null && File.ReadAllText(exe) == "MZ new version 0.2" && File.Exists(exe + ".old") && !File.Exists(exe + ".new"), "файл программы подменён, старый сохранён как .old " + (ierr ?? ""));
                    File.WriteAllText(src, "XX broken");
                    ri.AssetSize = new FileInfo(src).Length;
                    string bad = Updater.Install(ri, exe);
                    check(bad != null && File.ReadAllText(exe) == "MZ new version 0.2", "повреждённый файл не ставится: " + bad);
                    try { Directory.Delete(dir, true); } catch { }
                }

                // 3. Службы: отключить ненужные → откатить (проверяет остановку и возврат служб)
                if (SysInfo.IsAdmin)
                {
                    Tweak svc = Catalog.Resolve("system.services");
                    string svcBefore = svc.Capture();
                    JournalEntry se;
                    string serr = m.ApplyToggleWork(svc, true, out se);
                    check(serr == null && svc.ReadEnabled(), "ненужные службы отключены " + (serr ?? ""));
                    m.CommitFromUi(se);
                    m.Undo(se);
                    check(svc.Capture() == svcBefore, "службы вернулись в исходное состояние");
                }

                // Видеокарты: распознавание модели для игрового рейтинга
                string[] gpuNames = { "NVIDIA GeForce RTX 4070", "NVIDIA GeForce RTX 4070 Ti SUPER", "NVIDIA GeForce RTX 4060 Laptop GPU", "AMD Radeon RX 6800M",
                    "AMD Radeon RX 7600 XT", "Intel(R) Iris(R) Xe Graphics", "AMD Radeon(TM) 780M Graphics", "NVIDIA GeForce GTX 1650", "Intel(R) UHD Graphics 770" };
                int[] gpuWant = { 1000, 1250, 480, 750, 680, 90, 230, 250, 40 };
                bool[] gpuIgpu = { false, false, false, false, false, true, true, false, true };
                for (int i = 0; i < gpuNames.Length; i++)
                {
                    bool ig; int sc = GpuDb.ScoreOf(gpuNames[i], out ig);
                    check(sc == gpuWant[i] && ig == gpuIgpu[i], "видеокарта «" + gpuNames[i] + "» → " + sc + (ig ? " (встроенная)" : ""));
                }
                SysInfo.LoadHardware();
                { bool kn, ig; check(true, "эта видеокарта: " + string.Join(", ", SysInfo.Gpus.ToArray()) + " → " + GpuDb.Score(out kn, out ig) + (ig ? " (встроенная)" : "")); }

                // Discord: аппаратное ускорение — на копии настроек во временной папке, настоящий Discord не трогаем
                {
                    DiscordHwTweak dt = (DiscordHwTweak)Catalog.Resolve("apps.discord.hw");
                    check(true, "Discord на этом ПК: " + (DiscordHwTweak.Installed ? "установлен, ускорение " + (dt.ReadEnabled() ? "уже выключено" : "включено") : "не установлен"));
                    string tmpRoot = Path.Combine(Path.GetTempPath(), "tunex-discord-test");
                    string dir = Path.Combine(tmpRoot, "discord");
                    if (Directory.Exists(tmpRoot)) Directory.Delete(tmpRoot, true);
                    Directory.CreateDirectory(dir);
                    string sf = Path.Combine(dir, "settings.json");
                    File.WriteAllText(sf, "{\n  \"IS_MAXIMIZED\": true,\n  \"WINDOW_BOUNDS\": { \"x\": 112, \"width\": 1286 },\n  \"BACKGROUND_COLOR\": \"#121214\",\n  \"enableHardwareAcceleration\": true\n}");
                    Environment.SetEnvironmentVariable("TUNEX_DISCORD_DIR", tmpRoot);
                    try
                    {
                        check(dt.IsAvailable() && !dt.ReadEnabled(), "Discord (копия): ускорение включено — настройка не применена");
                        string snap = dt.Capture();
                        dt.Apply(true);
                        string after = File.ReadAllText(sf);
                        check(dt.ReadEnabled() && after.Contains("\"enableHardwareAcceleration\":false"), "Discord (копия): ускорение выключено");
                        check(after.Contains("\"width\":1286") && after.Contains("#121214") && after.Contains("\"IS_MAXIMIZED\":true"), "Discord (копия): остальные настройки сохранены");
                        check(File.Exists(sf + ".tunex-backup"), "Discord (копия): сохранена резервная копия файла");
                        dt.Restore(snap);
                        check(!dt.ReadEnabled() && File.ReadAllText(sf).Contains("\"enableHardwareAcceleration\":true"), "Discord (копия): откат вернул «true»");
                    }
                    finally { Environment.SetEnvironmentVariable("TUNEX_DISCORD_DIR", null); try { Directory.Delete(tmpRoot, true); } catch { } }
                }

                // Оценка ПК: сбор характеристик и узкие места (только чтение)
                {
                    PcSpecs ps = PcCheck.Collect();
                    check(ps.Ram.Count > 0 && ps.Disks.Count > 0 && ps.Gpus.Count > 0, "оценка ПК: память " + ps.Ram.Count + " шт., дисков " + ps.Disks.Count + ", видеокарт " + ps.Gpus.Count + ", экранов " + ps.Monitors.Count);
                    foreach (MemInfo mi in ps.Ram) log.AppendLine("      память: " + PcCheck.Gb(mi.Capacity) + " " + mi.TypeName + " " + mi.Configured + "/" + mi.Speed + " МГц, слот «" + mi.Locator + "», канал " + (mi.ChannelKey ?? "?"));
                    foreach (DiskInfo di in ps.Disks) log.AppendLine("      диск: " + di.Name + " — " + di.Kind + " [" + di.Letters + "]" + (di.Free >= 0 ? ", свободно " + PcCheck.Gb(di.Free) : "") + (di.System ? ", системный" : ""));
                    foreach (MonitorInfo mo in ps.Monitors) log.AppendLine("      экран: " + mo.Name + " " + mo.Hz + "/" + mo.MaxHz + " Гц, подключён к «" + mo.Adapter + "»");
                    foreach (GpuInfo gi in ps.Gpus) log.AppendLine("      видеокарта: " + gi.Name + (gi.Integrated ? " (встроенная)" : "") + ", код устройства " + gi.Error);
                    log.AppendLine("      ноутбук: " + ps.Laptop + ", от батареи: " + ps.OnBattery + ", экономия заряда: " + ps.BatterySaver + ", режим энергоэффективности: " + ps.EfficiencyMode);
                    log.AppendLine("      файл подкачки выключен: " + ps.PagefileOff + " (известно: " + ps.PagefileKnown + "), Wi-Fi при свободном кабеле: " + ps.WifiWithPort + ", без драйвера: " + ps.BasicAdapter);
                    log.AppendLine("      игр найдено: " + ps.Games.Count);
                    List<string> passedReal = new List<string>();
                    foreach (Finding fi in PcCheck.Analyze(ps, passedReal)) log.AppendLine("      проблема [" + fi.Level + (fi.Hidden ? ", скрытая" : "") + "]: " + fi.Title);
                    log.AppendLine("      в порядке: " + string.Join(", ", passedReal.ToArray()));
                    // Правила на выдуманных данных: одна планка + встроенная графика, HDD, 60 из 144 Гц
                    PcSpecs fake = new PcSpecs(); fake.Threads = 8;
                    MemInfo one = new MemInfo(); one.Capacity = 8L << 30; one.Speed = 3200; one.Configured = 2400; one.Type = 26; fake.Ram.Add(one);
                    GpuInfo ig = new GpuInfo(); ig.Name = "Intel(R) UHD Graphics"; ig.Integrated = true; fake.Gpus.Add(ig);
                    DiskInfo hd = new DiskInfo(); hd.Name = "WDC WD10"; hd.Hdd = true; hd.System = true; hd.Size = 1L << 40; hd.Free = 5L << 30; fake.Disks.Add(hd);
                    MonitorInfo fm = new MonitorInfo(); fm.Name = "Test"; fm.Device = "\\\\.\\DISPLAY9"; fm.Hz = 60; fm.Rates.Add(60); fm.Rates.Add(144); fake.Monitors.Add(fm);
                    string all = ""; foreach (Finding fi in PcCheck.Analyze(fake)) all += "[" + fi.Level + "] " + fi.Title + "; ";
                    check(all.Contains("[Critical] Память работает в одноканальном") && all.Contains("[Critical] Windows стоит на жёстком") && all.Contains("[Critical] Монитор работает на 60")
                        && all.Contains("[Warning] Память работает на 2400") && all.Contains("[Warning] На системном диске мало места") && all.Contains("[Warning] Оперативной памяти 8 ГБ"),
                        "правила узких мест на выдуманном ПК: " + all);
                    // Скрытые проблемы: монитор в материнской плате, игры на HDD при SSD, выключенный файл подкачки, нет драйвера, Wi-Fi
                    PcSpecs hid = HiddenDemo();
                    List<string> hidOk = new List<string>();
                    string hidAll = ""; int hidden = 0;
                    foreach (Finding fi in PcCheck.Analyze(hid, hidOk)) { hidAll += "[" + fi.Level + "] " + fi.Title + "; "; if (fi.Hidden) hidden++; }
                    check(hidAll.Contains("[Critical] Монитор подключён к материнской плате") && hidAll.Contains("[Warning] Игры лежат на жёстком диске, хотя есть SSD")
                        && hidAll.Contains("[Critical] Файл подкачки выключен") && hidAll.Contains("[Critical] Не установлен драйвер видеокарты")
                        && hidAll.Contains("[Critical] Видеокарта отключена: AMD Radeon RX 580") && hidAll.Contains("[Info] Интернет по Wi-Fi") && !hidAll.Contains("Жёсткий диск: ST2000")
                        && hidden == 6 && hidOk.Contains("частота экрана") && hidOk.Contains("питание") && !hidOk.Contains("кабель монитора"),
                        "скрытые проблемы на выдуманном ПК: " + hidAll + " в порядке: " + string.Join(", ", hidOk.ToArray()));
                    // Ноутбук: внутренний экран на встроенной графике — это нормально
                    hid.Laptop = true;
                    check(!string.Join(";", Titles(PcCheck.Analyze(hid))).Contains("материнской плате"), "ноутбук: экран на встроенной графике не считается ошибкой");

                    MemInfo a1 = new MemInfo(); a1.Locator = "DIMM_A1"; MemInfo a2 = new MemInfo(); a2.Locator = "DIMM_A2"; MemInfo b2 = new MemInfo(); b2.Locator = "DIMM_B2";
                    check(a1.ChannelKey == a2.ChannelKey && a1.ChannelKey != b2.ChannelKey, "каналы памяти по названию слотов: A1=" + a1.ChannelKey + " A2=" + a2.ChannelKey + " B2=" + b2.ChannelKey);
                }

                // Перед игрой: названия серверов, статистика пинга, итог, список программ, «Не беспокоить» (только чтение)
                {
                    check(NetPing.City("Stockholm - Kista (Sweden)") == "Стокгольм" && NetPing.City("Datapacket Frankfurt (Germany)") == "Франкфурт"
                        && NetPing.City("Alibaba Cloud Beijing - Mobile (China)") == "Пекин (Mobile)" && NetPing.City("Tokyo Koto City (Japan)") == "Токио",
                        "пинг: названия городов из списка Valve");
                    PingTarget pt = new PingTarget(); pt.Sent = 20; pt.Lost = 1; foreach (int v in new int[] { 40, 44, 40, 48 }) pt.Rtt.Add(v);
                    check(Math.Abs(pt.Avg - 43) < 0.01 && Math.Abs(pt.Jitter - 16f / 3) < 0.01 && Math.Abs(pt.Loss - 5) < 0.01 && pt.Unstable,
                        "пинг: средний " + pt.Avg + ", скачки " + pt.Jitter + ", потери " + pt.Loss + "%");
                    PingTarget good = new PingTarget(); good.Name = "Стокгольм"; good.Sent = 20; for (int i = 0; i < 20; i++) good.Rtt.Add(38 + i % 2);
                    PingTarget wifi = new PingTarget(); wifi.Router = true; wifi.Sent = 20; wifi.Lost = 2; for (int i = 0; i < 18; i++) wifi.Rtt.Add(i % 2 == 0 ? 3 : 40);
                    PingTarget lan = new PingTarget(); lan.Router = true; lan.Sent = 20; for (int i = 0; i < 20; i++) lan.Rtt.Add(1);
                    string vt; System.Drawing.Color vc;
                    string v1 = NetPing.Verdict("cs2", new List<PingTarget> { good }, lan, out vt, out vc);
                    string v2 = NetPing.Verdict("cs2", new List<PingTarget> { good }, wifi, out vt, out vc);
                    string v3 = NetPing.Verdict("cs2", new List<PingTarget>(), null, out vt, out vc);
                    check(v1 == "Интернет отлично подходит для CS2" && v2 == "Проблема в домашней сети" && v3 == "Серверы CS2 не ответили", "пинг: итог — " + v1 + " / " + v2 + " / " + v3);
                    List<PingTarget> valve = NetPing.Servers("cs2");
                    check(valve.Count >= 10, "пинг: серверов CS2 в списке " + valve.Count + (valve.Count > 0 ? " (" + valve[0].Name + "…)" : ""));
                    check(NetPing.Servers("valorant").Count >= 10, "пинг: дата-центры для Valorant");
                    check(NetPing.Servers("deadlock").Count >= 10 && NetPing.Servers("apex").Count >= 10 && NetPing.Servers("rust").Count >= 10 && NetPing.Approx("rust") && !NetPing.Approx("deadlock"), "пинг: Deadlock (список Valve), Apex и Rust");

                    List<AppGroup> apps = Unload.Scan();
                    bool bad = false;
                    foreach (AppGroup a in apps) { log.AppendLine("      программа: " + a.Name + " [" + a.Kind + "] " + Unload.Size(a.Memory) + ", окон " + a.Windows.Count); if (a.Key == "explorer" || a.Key == "tunex" || a.Key == "steam") bad = true; }
                    check(!bad, "разгрузка: найдено программ " + apps.Count + ", системных и Steam среди них нет");
                    // Закрыть и вернуть по-настоящему — на Блокноте
                    foreach (System.Diagnostics.Process old in System.Diagnostics.Process.GetProcessesByName("notepad")) old.Dispose();
                    if (System.Diagnostics.Process.GetProcessesByName("notepad").Length == 0)
                    {
                        System.Diagnostics.Process np = System.Diagnostics.Process.Start("notepad.exe");
                        np.WaitForInputIdle(5000); System.Threading.Thread.Sleep(800);
                        AppGroup ng = Unload.Probe("notepad");
                        List<string> nf = new List<string>();
                        List<ClosedApp> nc = Unload.Close(new List<AppGroup> { ng }, nf);
                        check(ng.Windows.Count > 0 && nc.Count == 1 && nf.Count == 0 && System.Diagnostics.Process.GetProcessesByName("notepad").Length == 0,
                            "разгрузка: Блокнот закрыт как крестиком (окон было " + ng.Windows.Count + ")");
                        List<string> back = Unload.Restore(nc);
                        System.Threading.Thread.Sleep(2500);
                        System.Diagnostics.Process[] again = System.Diagnostics.Process.GetProcessesByName("notepad");
                        check(back.Count == 1 && again.Length > 0, "разгрузка: Блокнот открыт снова");
                        foreach (System.Diagnostics.Process x in again) { try { x.Kill(); } catch { } x.Dispose(); }
                    }
                    else log.AppendLine("      разгрузка: Блокнот уже открыт — проверку закрытия пропускаю");

                    int focus = QuietMode.ReadFocus();
                    check(focus >= 0 && focus <= 2, "не беспокоить: режим уведомлений читается (сейчас " + focus + ")");
                    if (focus >= 0 && focus != 2)
                    {
                        bool on = QuietMode.SetFocus(2);
                        bool off = QuietMode.SetFocus(focus);
                        check(on && off, "не беспокоить: включается и возвращается обратно");
                    }
                }

                // Разбор лагов: выдуманные 2 минуты → причины; настоящая запись процессов и дисков
                {
                    LagMoment d1 = LagAnalyzer.Analyze(FakeLag("disk"), "cs2");
                    check(d1.Main.Title.StartsWith("Диск D: был занят") && d1.Main.Text.Contains("Защитник Windows") && d1.Main.At >= 100,
                        "лаги: диск и Защитник — " + d1.Main.Title + " / " + d1.Main.Text);
                    LagMoment d2 = LagAnalyzer.Analyze(FakeLag("cpu"), "cs2");
                    check(d2.Main.Title == "Google Chrome занимал процессор на 45%" && d2.Main.Action == "prep", "лаги: браузер занял процессор — " + d2.Main.Title);
                    LagMoment d3 = LagAnalyzer.Analyze(FakeLag("ram"), "cs2");
                    check(d3.Main.Title == "Закончилась оперативная память" && d3.Main.Text.Contains("игра (5,5 ГБ)"), "лаги: память — " + d3.Main.Text);
                    LagMoment d4 = LagAnalyzer.Analyze(FakeLag("calm"), "cs2");
                    check(d4.Calm && d4.Causes.Count == 1, "лаги: спокойный ПК — " + d4.Main.Title);
                    LagMoment d5 = LagAnalyzer.Analyze(FakeLag("game"), "cs2");
                    check(d5.Main.Text.StartsWith("Игра сама подгружала") && d5.Main.Action == "pc", "лаги: игра читает с диска — " + d5.Main.Text);
                    LagMoment d6 = LagAnalyzer.Analyze(FakeLag("update"), "cs2");
                    bool upd = false; foreach (LagCause c in d6.Causes) if (c.Title == "В этот момент запустилась программа: Обновления Windows") upd = true;
                    check(upd, "лаги: запуск обновлений Windows замечен");
                    string js = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(d1);
                    LagMoment back = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<LagMoment>(js);
                    check(back.Causes.Count == d1.Causes.Count && back.Cpu.Length == 120 && back.Main.Title == d1.Main.Title && !js.Contains("\"Main\""), "лаги: разбор сохраняется и читается (" + js.Length / 1024 + " КБ)");

                    for (int i = 0; i < 3; i++) { LagRecorder.TickNow(); System.Threading.Thread.Sleep(1000); }
                    List<LagFrame> fr = LagRecorder.Frames;
                    LagFrame lf = fr.Count > 0 ? fr[fr.Count - 1] : null;
                    check(fr.Count >= 2 && lf.Top.Count > 0 && lf.AvailGb > 0, "лаги: запись идёт, кадров " + fr.Count + (lf != null ? ", активных программ " + lf.Top.Count : ""));
                    if (lf != null)
                    {
                        log.AppendLine("      диск " + (lf.DiskName ?? "?") + " занят " + lf.Disk + "%, чтение/запись " + lf.DiskMb.ToString("0.0") + " МБ/с, ошибок страниц " + lf.Faults.ToString("0") + "/с");
                        foreach (ProcLoad pl in lf.Top) log.AppendLine("      " + pl.Name + ": процессор " + pl.Cpu.ToString("0.0") + "%, диск " + pl.DiskMb.ToString("0.00") + " МБ/с, память " + pl.MemGb.ToString("0.00") + " ГБ");
                    }
                    check(lf != null && lf.Disk >= 0, "лаги: занятость дисков читается");
                    LagRecorder.Stop();
                }

                // Обслуживание: частота мыши, очистка (на временной папке), автозагрузка (на выдуманной записи), пинг под нагрузкой
                {
                    foreach (int hz in new int[] { 125, 500, 1000 })
                    {
                        MouseRate mr = new MouseRate(); long tk = 0; Random rnd = new Random(hz);
                        for (int i = 0; i < hz * 4; i++)
                        {
                            tk += System.Diagnostics.Stopwatch.Frequency / hz + rnd.Next(-200, 200);
                            if (i % (hz / 2) == hz / 4) tk += System.Diagnostics.Stopwatch.Frequency / 5;   // мышь остановилась
                            mr.Add(tk);
                        }
                        int got = mr.Measured();
                        check(MouseRate.Nominal(got) == hz, "мышь: " + hz + " Гц определена как " + got + " → " + MouseRate.Nominal(got));
                    }

                    string dir = Path.Combine(Path.GetTempPath(), "tunex-clean-test");
                    try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
                    Directory.CreateDirectory(Path.Combine(dir, @"a\b"));
                    string oldF = Path.Combine(dir, @"a\b\old.tmp"), newF = Path.Combine(dir, "new.tmp");
                    File.WriteAllBytes(oldF, new byte[5000]); File.SetLastWriteTime(oldF, DateTime.Now.AddDays(-10));
                    File.WriteAllBytes(newF, new byte[3000]);
                    CleanItem ci = new CleanItem(); ci.Key = "test"; ci.MinAgeHours = 168; ci.Dirs.Add(dir);
                    Cleaner.Measure(ci);
                    check(ci.Size == 5000 && ci.Count == 1, "очистка: найден только старый файл (" + ci.Size + " байт)");
                    long fr = Cleaner.Clean(ci);
                    check(fr == 5000 && !File.Exists(oldF) && File.Exists(newF) && !Directory.Exists(Path.Combine(dir, "a")) && Directory.Exists(dir), "очистка: старый удалён, новый и сама папка на месте");
                    try { Directory.Delete(dir, true); } catch { }
                    List<CleanItem> items = Cleaner.Items();
                    foreach (CleanItem x in items) { Cleaner.Measure(x); log.AppendLine("      чистка: " + x.Title + " — " + Unload.Size(x.Size) + (x.Blocked != null ? " (" + x.Blocked + ")" : "")); }
                    check(items.Count >= 4, "очистка: разделов " + items.Count);

                    check(Startup.ExeOf(@"""C:\Program Files\App\app.exe"" --min") ==@"C:\Program Files\App\app.exe"
                        && Startup.ExeOf(@"C:\Tools\run.exe /silent") == @"C:\Tools\run.exe", "автозагрузка: путь к программе из команды");
                    const string run = @"Software\Microsoft\Windows\CurrentVersion\Run";
                    Reg.Set("HKCU", run, "TunexSelfTest", @"C:\Windows\notepad.exe", Microsoft.Win32.RegistryValueKind.String);
                    try
                    {
                        StartupItem st = Startup.List().Find(delegate(StartupItem x) { return x.ValueName == "TunexSelfTest"; });
                        bool found = st != null && st.Enabled;
                        string e1 = st != null ? Startup.SetEnabled(st, false) : "нет";
                        StartupItem st2 = Startup.List().Find(delegate(StartupItem x) { return x.ValueName == "TunexSelfTest"; }); bool offRead = st2 != null && !st2.Enabled;
                        string e2 = st2 != null ? Startup.SetEnabled(st2, true) : "нет";
                        StartupItem st3 = Startup.List().Find(delegate(StartupItem x) { return x.ValueName == "TunexSelfTest"; });
                        check(found && e1 == null && st2 != null && offRead && e2 == null && st3 != null && st3.Enabled, "автозагрузка: выключить и включить (как в Диспетчере задач) — найдено " + found + ", ошибки " + e1 + "/" + e2 + ", после выключения " + (!offRead).ToString() + ", после включения " + (st3 != null ? st3.Enabled.ToString() : "нет"));
                    }
                    finally
                    {
                        Reg.Delete("HKCU", run, "TunexSelfTest");
                        Reg.Delete("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "TunexSelfTest");
                    }
                    List<StartupItem> all = Startup.List(); Startup.AttachSlow(all);
                    List<BootInfo> boots = Startup.Boots(5);
                    log.AppendLine("      автозагрузка: программ " + all.Count + ", загрузок в журнале " + boots.Count + (boots.Count > 0 ? ", последняя " + boots[0].Seconds + " с" : "") + (Startup.BootError != null ? ", " + Startup.BootError : ""));
                    foreach (StartupItem x in all) if (x.SlowSec >= 0) log.AppendLine("      замедляет: " + x.Name + " — " + x.SlowSec + " с, раз " + x.SlowCount);
                    check(all.Count > 0, "автозагрузка: список читается");

                    LoadResult lr = new LoadResult();
                    lr.Idle = new LoadPhase(); for (int i = 0; i < 20; i++) lr.Idle.Rtt.Add(15); lr.Idle.Sent = 20;
                    lr.Down = new LoadPhase(); lr.Down.Mbps = 90; for (int i = 0; i < 40; i++) lr.Down.Rtt.Add(i < 30 ? 160 : 20); lr.Down.Sent = 40;
                    lr.Up = new LoadPhase(); lr.Up.Mbps = 40; for (int i = 0; i < 40; i++) lr.Up.Rtt.Add(25); lr.Up.Sent = 40;
                    string lt; System.Drawing.Color lc;
                    string v1 = LoadTest.Verdict(lr, out lt, out lc);
                    lr.Down.Rtt.Clear(); for (int i = 0; i < 40; i++) lr.Down.Rtt.Add(30);
                    string v2 = LoadTest.Verdict(lr, out lt, out lc);
                    check(v1 == "Сильные лаги, когда интернет занят" && v2 == "Роутер справляется с нагрузкой", "пинг под нагрузкой: итог — " + v1 + " / " + v2);
                }

                // Игры: требования Steam
                {
                    string cm;
                    int my = CpuDb.ScoreOf("11th Gen Intel(R) Core(TM) i7-1165G7 @ 2.80GHz", 8, out cm);
                    check(my > 0 && cm == "Core i7-1165G7", "процессор «i7-1165G7» → " + cm + " = " + my);
                    string[] cpuOrder = { "Intel Core i9-14900K", "AMD Ryzen 7 5800X3D", "Intel Core i5-12400F", "AMD Ryzen 5 3600", "Intel Core i7-6700K", "AMD Ryzen 5 1400",
                        "Intel Core i5-6600", "Intel Core i5-2500K", "AMD FX-6300", "Intel Core 2 Duo E8400" };
                    string cl = ""; int prev = int.MaxValue; bool ordered = true;
                    foreach (string c in cpuOrder)
                    {
                        List<KeyValuePair<string, int>> f = CpuDb.FindInText(c);
                        int sc = f.Count == 1 ? f[0].Value : -1;
                        if (sc < 0 || sc > prev) ordered = false;
                        prev = sc; cl += (f.Count == 1 ? f[0].Key : "?") + "=" + sc + "; ";
                    }
                    check(ordered, "процессоры по убыванию: " + cl);
                    check(CpuDb.FindInText("Intel® Core™ i5-4460 / AMD FX™-6300 or better").Count == 2, "два процессора в одной строке требований");
                    check(CpuDb.FindInText("Dual core 2.4 GHz").Count == 0, "«Dual core 2.4 GHz» — модели нет, оценки нет");

                    List<KeyValuePair<string, int>> gf = GpuDb.FindInText("NVIDIA GeForce GTX 1060 3GB / AMD Radeon RX 470 / Intel Arc A580");
                    check(gf.Count == 3, "три видеокарты в строке требований: " + gf.Count);
                    gf = GpuDb.FindInText("Intel HD Graphics 4000 or GeForce 970");
                    check(gf.Count == 2 && gf.Exists(delegate(KeyValuePair<string, int> k) { return k.Key == "HD 4000"; }) && gf.Exists(delegate(KeyValuePair<string, int> k) { return k.Key == "GTX 970"; }),
                        "«HD Graphics 4000» и «GeForce 970» распознаны");
                    gf = GpuDb.FindInText("NVIDIA GeForce RTX 4070 Ti");
                    check(gf.Count == 1 && gf[0].Key == "RTX 4070 TI", "«RTX 4070 Ti» не считается ещё и как «RTX 4070»");
                    check(GpuDb.FindInText("1 GB VRAM, DirectX 11 compatible").Count == 0, "«1 GB VRAM, DirectX 11» — модели нет");

                    ReqLevel lv = SteamReq.ParseLevel("<strong>Minimum:</strong><br><ul class=\"bb_ul\"><li><strong>OS:</strong> Windows 10 64bit<br></li><li><strong>Processor:</strong> Intel Core i5-6600 or AMD Ryzen 5 1400<br></li>" +
                        "<li><strong>Memory:</strong> 12 GB RAM<br></li><li><strong>Graphics:</strong> GTX 1060 / RX 470<br></li></ul>");
                    check(lv != null && lv.RamGb == 12 && lv.Gpu == 230 && lv.Cpu > 0, "разбор HTML требований: память " + (lv == null ? "?" : lv.RamGb + " ГБ, видео " + lv.Gpu + " (" + lv.GpuModels + "), ЦП " + lv.Cpu + " (" + lv.CpuModels + ")"));

                    // Библиотека Steam: две папки, служебный пакет отбрасывается
                    string sroot = Path.Combine(Path.GetTempPath(), "tunex-steam-test");
                    if (Directory.Exists(sroot)) Directory.Delete(sroot, true);
                    string lib2 = Path.Combine(sroot, "lib2");
                    Directory.CreateDirectory(Path.Combine(sroot, "steamapps")); Directory.CreateDirectory(Path.Combine(lib2, "steamapps"));
                    File.WriteAllText(Path.Combine(sroot, @"steamapps\libraryfolders.vdf"), "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + sroot.Replace("\\", "\\\\") + "\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"" + lib2.Replace("\\", "\\\\") + "\"\n\t}\n}\n");
                    File.WriteAllText(Path.Combine(sroot, @"steamapps\appmanifest_228980.acf"), "\"AppState\"\n{\n\t\"appid\"\t\t\"228980\"\n\t\"name\"\t\t\"Steamworks Common Redistributables\"\n}\n");
                    File.WriteAllText(Path.Combine(lib2, @"steamapps\appmanifest_252490.acf"), "\"AppState\"\n{\n\t\"appid\"\t\t\"252490\"\n\t\"name\"\t\t\"Rust\"\n}\n");
                    Environment.SetEnvironmentVariable("TUNEX_STEAM_DIR", sroot);
                    try
                    {
                        List<SteamGame> inst = SteamLib.Installed();
                        check(inst.Count == 1 && inst[0].AppId == 252490 && inst[0].Name == "Rust", "библиотека Steam (копия): найдено " + inst.Count + ", служебный пакет пропущен");
                    }
                    finally { Environment.SetEnvironmentVariable("TUNEX_STEAM_DIR", null); try { Directory.Delete(sroot, true); } catch { } }
                    check(true, "Steam на этом ПК: " + (SteamLib.Present ? "установлен, игр " + SteamLib.Installed().Count : "не найден"));

                    // Таблица FPS: опорная система даёт опорные числа, сильнее видеокарта — не меньше FPS, видеокарты из таблицы известны
                    {
                        bool cb, eb; bool okRef = true, okMono = true; string unknown = "";
                        foreach (PopularGame pg in FpsData.Games)
                        {
                            int hi = FpsData.Estimate(pg, Preset.High1080, FpsData.RefGpu, FpsData.RefCpu, out cb, out eb);
                            int want = Math.Min(Math.Min(pg.High, pg.CpuCap), pg.EngineCap > 0 ? pg.EngineCap : int.MaxValue);
                            if (Math.Abs(hi - want) > 5) okRef = false;
                            int prevF = -1;
                            List<int> scores = new List<int>();
                            foreach (string gn in FpsData.TableGpus) scores.Add(FpsData.GpuScore(gn));
                            scores.Sort();
                            foreach (int gs in scores)
                            {
                                int f = FpsData.Estimate(pg, Preset.Low1080, gs, FpsData.RefCpu, out cb, out eb);
                                if (f < prevF - 5) okMono = false;
                                prevF = Math.Max(prevF, f);
                            }
                        }
                        foreach (string gn in FpsData.TableGpus) if (FpsData.GpuScore(gn) <= 0) unknown += gn + "; ";
                        foreach (string[] c in FpsData.TableCpus) if (FpsData.CpuScore(c[0]) <= 0) unknown += c[0] + "; ";
                        check(okRef && okMono && unknown == "", "таблица FPS: опорные числа " + (okRef ? "сходятся" : "НЕ сходятся") + ", рост с видеокартой " + (okMono ? "есть" : "НЕТ") + (unknown != "" ? ", неизвестны: " + unknown : ""));
                    }

                    // Живой запрос к Steam (без интернета — не ошибка, а «нет данных»)
                    PcProfile pp = PcProfile.From(PcCheck.Collect());
                    log.AppendLine("      этот ПК для игр: видео " + pp.Gpu + " (" + pp.GpuName + "), ЦП " + pp.Cpu + " (" + pp.CpuName + "), память " + Math.Round(pp.RamGb, 1) + " ГБ");
                    foreach (int id in new int[] { 252490, 730, 1172470 })
                    {
                        bool net; GameReq gr = SteamReq.Get(id, out net);
                        if (gr.Error != null) { log.AppendLine("      Steam " + id + ": " + gr.Error); continue; }
                        GameVerdict gv = GameVerdict.Make(pp, gr);
                        log.AppendLine("      Steam " + id + " «" + gr.Name + "» (" + (net ? "из сети" : "из кэша") + "): " + gv.Title);
                        foreach (PartResult pr in gv.Parts) log.AppendLine("          " + pr.Part + " — " + pr.Text);
                        if (gr.Min != null) log.AppendLine("          мин: ЦП «" + gr.Min.CpuText + "» → " + gr.Min.Cpu + ", видео «" + gr.Min.GpuText + "» → " + gr.Min.Gpu);
                        if (gr.Rec != null) log.AppendLine("          рек: ЦП «" + gr.Rec.CpuText + "» → " + gr.Rec.Cpu + ", видео «" + gr.Rec.GpuText + "» → " + gr.Rec.Gpu);
                    }
                }

                // 3. Электропитание: чтение без записи
                Tweak cpu = Catalog.Resolve("power.cpumin");
                check(cpu.IsAvailable(), "настройки схемы питания читаются: " + cpu.Capture());
                check(Power.Schemes().Count > 0, "схемы питания: " + Power.Schemes().Count + ", активная «" + Power.Name(Power.Active()) + "»");

                // 4. Каталог: каждая настройка читается без исключений
                foreach (KeyValuePair<string, Tweak> kv in Catalog.All)
                {
                    string state;
                    try { state = kv.Value.IsAvailable() ? (kv.Value.ReadEnabled() ? "вкл" : "выкл") : "недоступно: " + kv.Value.UnavailableReason; }
                    catch (Exception ex) { state = "ОШИБКА " + ex.Message; fails++; }
                    log.AppendLine("      " + kv.Value.Id + " = " + state);
                }
                sensors.Dispose();
            }
            catch (Exception ex) { log.AppendLine("FAIL  исключение: " + ex); fails++; }
            log.AppendLine(fails == 0 ? "ИТОГ: всё прошло" : "ИТОГ: ошибок " + fails);
            File.WriteAllText(reportPath, log.ToString(), Encoding.UTF8);
        }

        static List<string> Titles(List<Finding> list)
        {
            List<string> t = new List<string>();
            foreach (Finding f in list) t.Add(f.Title);
            return t;
        }

        // Выдуманный ПК со скрытыми проблемами — для самопроверки и для просмотра интерфейса (--pcdemo)
        // Выдуманные 2 минуты игры для самопроверки и --lagdemo
        public static List<LagFrame> FakeLag(string kind)
        {
            List<LagFrame> l = new List<LagFrame>();
            Random r = new Random(7);
            DateTime t0 = DateTime.Now.AddSeconds(-120);
            for (int i = 0; i < 120; i++)
            {
                LagFrame f = new LagFrame(); f.T = t0.AddSeconds(i);
                bool hot = i >= 104 && i <= 111;
                f.Cpu = 38 + r.Next(10); f.Gpu = 70 + r.Next(15); f.Ram = 62 + r.Next(3); f.AvailGb = 5.8f; f.CpuTemp = 68; f.GpuTemp = 66;
                f.Disk = 3 + r.Next(8); f.DiskName = "D:";
                ProcLoad game = new ProcLoad(); game.Name = "cs2"; game.Cpu = 30 + r.Next(6); game.MemGb = 3.1f; game.DiskMb = 0.4f; f.Top.Add(game);
                ProcLoad dc = new ProcLoad(); dc.Name = "discord"; dc.Cpu = 1.5f; dc.MemGb = 0.4f; f.Top.Add(dc);
                if (hot && kind == "disk") { f.Disk = 96 + r.Next(5); ProcLoad d = new ProcLoad(); d.Name = "msmpeng"; d.Cpu = 9; d.DiskMb = 85; d.MemGb = 0.3f; f.Top.Add(d); }
                if (hot && kind == "game") { f.Disk = 99; game.DiskMb = 120; }
                if (hot && kind == "cpu") { f.Cpu = 96; ProcLoad c = new ProcLoad(); c.Name = "chrome"; c.Cpu = 40 + (i == 107 ? 5 : 0); c.MemGb = 1.9f; f.Top.Add(c); }
                if (hot && kind == "ram") { f.Ram = 97; f.AvailGb = 0.25f; f.Faults = 900; game.MemGb = 5.5f; ProcLoad c = new ProcLoad(); c.Name = "chrome"; c.MemGb = 2.2f; c.Cpu = 3; f.Top.Add(c); }
                if (kind == "update" && i == 100) f.Started.Add("tiworker");
                l.Add(f);
            }
            return l;
        }

        public static LagMoment LagDemo()
        {
            LagMoment m = LagAnalyzer.Analyze(FakeLag("disk"), "cs2");
            m.Demo = true;
            return m;
        }

        public static PcSpecs HiddenDemo()
        {
            PcSpecs hid = new PcSpecs(); hid.Threads = 12;
            GpuInfo hi = new GpuInfo(); hi.Name = "Intel(R) UHD Graphics 770"; hi.Integrated = true; hid.Gpus.Add(hi);
            GpuInfo hd2 = new GpuInfo(); hd2.Name = "NVIDIA GeForce RTX 3060"; hd2.DriverDate = DateTime.Now.AddDays(-30); hid.Gpus.Add(hd2);
            GpuInfo off = new GpuInfo(); off.Name = "AMD Radeon RX 580"; off.Error = 22; hid.Gpus.Add(off);
            MonitorInfo hm = new MonitorInfo(); hm.Name = "Samsung"; hm.Device = "\\\\.\\DISPLAY8"; hm.Hz = 144; hm.Rates.Add(144); hm.Adapter = "Intel(R) UHD Graphics 770"; hid.Monitors.Add(hm);
            DiskInfo sd = new DiskInfo(); sd.Name = "Samsung 980"; sd.Ssd = true; sd.System = true; sd.Letters = "C"; sd.Size = 1L << 40; sd.Free = 400L << 30; hid.Disks.Add(sd);
            DiskInfo hh = new DiskInfo(); hh.Name = "ST2000"; hh.Hdd = true; hh.Letters = "D"; hh.Size = 2L << 40; hid.Disks.Add(hh);
            foreach (string gn in new string[] { "Cyberpunk 2077", "Rust", "Dota 2", "Elden Ring" }) { GameSpot gs = new GameSpot(); gs.Name = gn; gs.Letter = 'D'; hid.Games.Add(gs); }
            GameSpot gc = new GameSpot(); gc.Name = "Counter-Strike 2"; gc.Letter = 'C'; hid.Games.Add(gc);
            hid.PagefileKnown = true; hid.PagefileOff = true; hid.BasicAdapter = true; hid.WifiWithPort = true; hid.NetKnown = true;
            return hid;
        }
    }
}
