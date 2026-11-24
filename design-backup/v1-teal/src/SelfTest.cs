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
