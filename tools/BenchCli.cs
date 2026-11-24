namespace Tunex {
static class BenchCli {
  static void Main(string[] a) {
    SysInfo.InitFast(); SysInfo.LoadHardware();
    BenchResult r = Bench.Run(delegate(string s, int p) { System.Console.WriteLine(p + "% " + s); }, delegate { return false; });
    System.Console.WriteLine("single raw=" + r.CpuSingleRaw.ToString("0") + " score=" + r.CpuSingle);
    System.Console.WriteLine("multi raw=" + r.CpuMultiRaw.ToString("0") + " score=" + r.CpuMulti + " threads=" + r.Threads);
    System.Console.WriteLine("ram " + r.RamGBps.ToString("0.0") + " GB/s score=" + r.RamScore);
    System.Console.WriteLine("disk w=" + r.DiskWriteMBps.ToString("0") + " r=" + r.DiskReadMBps.ToString("0") + " score=" + r.DiskScore);
    System.Console.WriteLine("total=" + r.Total + " " + Bench.Tier(r.Total) + " gpu=" + Bench.GpuTier());
  } } }
