using System;
namespace Tunex {
static class PowerCli {
  static void Main(string[] a) {
    SysInfo.InitFast();
    Guid sub = new Guid("54533251-82be-4824-96c1-47b60b740d00"), min = new Guid("893dee8e-2bef-41e0-89c6-b55d0929964c");
    Console.WriteLine("S0 (современный сон): " + Power.IsModernStandby());
    Guid before = Power.Active();
    Guid g = Power.CreateMaxPerformance();
    Console.WriteLine("создана: " + g + " имя='" + Power.Name(g) + "' мин.ЦП=" + Power.ReadAC(g, sub, min));
    Power.SetActive(g);
    Console.WriteLine("активна наша: " + (Power.Active() == g));
    Power.SetActive(before);
    Console.WriteLine("вернули: " + (Power.Active() == before));
    System.Diagnostics.Process.Start("powercfg", "-delete " + g).WaitForExit();
    // Чиним схему, созданную старой версией кнопки
    Guid old = new Guid("a0632a99-99cc-4131-8c20-bab21e3cf60a");
    if (Power.Schemes().Contains(old)) { Power.SetName(old, Power.OwnName); Power.ApplyMaxValues(old); Console.WriteLine("старая схема исправлена: '" + Power.Name(old) + "' мин.ЦП=" + Power.ReadAC(old, sub, min)); }
    Console.WriteLine("режим питания поддерживается: " + Power.OverlaySupported());
    if (Power.OverlaySupported()) {
      Guid ov = Power.Overlay();
      Power.SetOverlay(Power.OverlayBest); Guid now = Power.Overlay();
      Power.SetOverlay(ov);
      Console.WriteLine("режим: было " + ov + " → макс " + (now == Power.OverlayBest) + " → вернули " + (Power.Overlay() == ov));
    }
  } } }
