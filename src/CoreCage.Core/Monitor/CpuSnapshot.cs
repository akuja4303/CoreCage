using System.Collections.Generic;
using System.Linq;

namespace CoreCage.Core.Monitor
{
    public readonly struct SensorReading
    {
        public readonly string Name;
        public readonly string Type;
        public readonly float Value;
        public SensorReading(string name, string type, float value) { Name = name; Type = type; Value = value; }
    }

    public class CoreInfo
    {
        public int Index;
        public float ClockMhz;
        public float PowerW;
        public float Vid;
        public float LoadPct;
    }

    public class CpuSnapshot
    {
        public string Name = "";
        public CoreInfo[] Cores = System.Array.Empty<CoreInfo>();
        public float Vcore;
        public float SocV;
        public float PackagePowerW;
        public float TctlC;

        public static CpuSnapshot BuildSnapshot(IEnumerable<SensorReading> readings, string cpuName = "")
        {
            var cores = new Dictionary<int, CoreInfo>();
            var threadLoads = new Dictionary<int, float>();
            var snap = new CpuSnapshot { Name = cpuName };

            CoreInfo Core(int i)
            {
                if (!cores.TryGetValue(i, out var c)) { c = new CoreInfo { Index = i }; cores[i] = c; }
                return c;
            }

            foreach (var r in readings)
            {
                switch (r.Type)
                {
                    case "Clock":
                        if (r.Name.StartsWith("Core #") && TryNum(r.Name, out int cc)) Core(cc).ClockMhz = r.Value;
                        break;
                    case "Power":
                        if (r.Name == "Package") snap.PackagePowerW = r.Value;
                        else if (r.Name.StartsWith("Core #") && TryNum(r.Name, out int cp)) Core(cp).PowerW = r.Value;
                        break;
                    case "Voltage":
                        if (r.Name == "Core (SVI2 TFN)") snap.Vcore = r.Value;
                        else if (r.Name == "SoC (SVI2 TFN)") snap.SocV = r.Value;
                        else if (r.Name.StartsWith("Core #") && r.Name.Contains("VID") && TryNum(r.Name, out int cv)) Core(cv).Vid = r.Value;
                        break;
                    case "Temperature":
                        if (r.Name.Contains("Tctl") || r.Name.Contains("Tdie")) snap.TctlC = r.Value;
                        break;
                    case "Load":
                        if (r.Name.StartsWith("CPU Core #") && TryNum(r.Name, out int th)) threadLoads[th] = r.Value;
                        break;
                }
            }

            foreach (var t in threadLoads.Keys) Core((t + 1) / 2);
            foreach (var core in cores.Values)
            {
                var vals = new List<float>();
                if (threadLoads.TryGetValue(core.Index * 2 - 1, out var l1)) vals.Add(l1);
                if (threadLoads.TryGetValue(core.Index * 2, out var l2)) vals.Add(l2);
                if (vals.Count > 0) core.LoadPct = vals.Average();
            }

            snap.Cores = cores.Values.OrderBy(c => c.Index).ToArray();
            return snap;
        }

        private static bool TryNum(string name, out int n)
        {
            n = 0;
            int hash = name.IndexOf('#');
            if (hash < 0) return false;
            int i = hash + 1, val = 0; bool any = false;
            while (i < name.Length && char.IsDigit(name[i])) { val = val * 10 + (name[i] - '0'); i++; any = true; }
            if (any) { n = val; return true; }
            return false;
        }
    }
}
