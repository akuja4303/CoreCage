using System.Linq;

namespace CoreCage.Core.Monitor
{
    public static class CpuStats
    {
        public static int PreferredCore(CoreInfo[] cores)
        {
            if (cores == null || cores.Length == 0) return -1;
            return cores.OrderByDescending(c => c.ClockMhz).First().Index;
        }
    }
}
