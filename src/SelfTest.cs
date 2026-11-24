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
                    foreach (DiskInfo di in ps.Disks) log.AppendLine("      диск: " + di.Name + " — " + di.Kind + (di.System ? ", системный, свободно " + PcCheck.Gb(di.Free) : ""));
                    foreach (MonitorInfo mo in ps.Monitors) log.AppendLine("      экран: " + mo.Name + " " + mo.Hz + "/" + mo.MaxHz + " Гц");
                    log.AppendLine("      ноутбук: " + ps.Laptop + ", от батареи: " + ps.OnBattery);
                    foreach (Finding fi in PcCheck.Analyze(ps)) log.AppendLine("      узкое место [" + fi.Level + "]: " + fi.Title);
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
                    MemInfo a1 = new MemInfo(); a1.Locator = "DIMM_A1"; MemInfo a2 = new MemInfo(); a2.Locator = "DIMM_A2"; MemInfo b2 = new MemInfo(); b2.Locator = "DIMM_B2";
                    check(a1.ChannelKey == a2.ChannelKey && a1.ChannelKey != b2.ChannelKey, "каналы памяти по названию слотов: A1=" + a1.ChannelKey + " A2=" + a2.ChannelKey + " B2=" + b2.ChannelKey);
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
    }
}
