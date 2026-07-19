using System;
using System.Collections.Generic;

namespace CoreCage.Core
{
    /// <summary>CPU tuning parameters exposed as sliders. Units match SetRyzenPowerLimits arguments.</summary>
    public enum TuningParam { CoAll, Stapm, FastPpt, SlowPpt, Tdc, Edc, Tctl }

    /// <summary>Inclusive [Min,Max] bound for a tuning parameter plus its validated default.</summary>
    public struct TuningRange
    {
        public int Min;
        public int Max;
        public int Default;
        public TuningRange(int min, int max, int def) { Min = min; Max = max; Default = def; }
    }

    /// <summary>The seven CPU knobs the Tuner exposes. Watts/amps in human units; conversion in BuildRyzenAdjArgs.</summary>
    public class CpuTuningValues
    {
        public int CoAll;   // 0 .. -30
        public int StapmW;  // 45 .. 95
        public int FastW;   // 65 .. 105
        public int SlowW;   // 45 .. 95
        public int TdcA;    // 50 .. 80 (0 = omit)
        public int EdcA;    // 80 .. 120 (0 = omit)
        public int TctlC;   // 70 .. 90
    }

    /// <summary>One parsed line of ryzenadj stdout. Ok=false means the parameter was silently skipped.</summary>
    public struct RyzenAdjResult
    {
        public string Param;
        public bool Ok;
        public RyzenAdjResult(string param, bool ok) { Param = param; Ok = ok; }
    }

    /// <summary>Pure tuning helpers: safe ranges, clamping, RyzenAdj arg-building, output parsing.
    /// No WPF and no I/O — fully unit-testable.</summary>
    public static class TuningState
    {
        /// <summary>5600G-safe inclusive ranges. CoAll is negative-only.</summary>
        public static TuningRange Range(TuningParam p)
        {
            switch (p)
            {
                case TuningParam.CoAll:   return new TuningRange(-30, 0, -20);
                case TuningParam.Stapm:   return new TuningRange(45, 95, 95);
                case TuningParam.FastPpt: return new TuningRange(65, 105, 105);
                case TuningParam.SlowPpt: return new TuningRange(45, 95, 95);
                case TuningParam.Tdc:     return new TuningRange(50, 80, 75);
                case TuningParam.Edc:     return new TuningRange(80, 120, 110);
                case TuningParam.Tctl:    return new TuningRange(70, 90, 90);
                default: throw new ArgumentOutOfRangeException(nameof(p));
            }
        }

        /// <summary>Clamps a value into the parameter's safe range.</summary>
        public static int Clamp(TuningParam p, int value)
        {
            var r = Range(p);
            if (value < r.Min) return r.Min;
            if (value > r.Max) return r.Max;
            return value;
        }

        /// <summary>Validated-stable gaming defaults (CO-20, 2h CoreCycler / 0 WHEA on this 5600G).</summary>
        public static CpuTuningValues ValidatedDefaults()
        {
            return new CpuTuningValues
            {
                CoAll = -20, StapmW = 95, FastW = 105, SlowW = 95, TdcA = 75, EdcA = 110, TctlC = 90
            };
        }

        /// <summary>Builds the exact ryzenadj.exe argument string. Clamps every field to its safe range first.
        /// Units: STAPM/Fast/Slow watts -> mW (*1000); tctl in °C; TDC/EDC amps -> mA (*1000);
        /// CoAll negative offset encoded as 0x100000 + offset. CoAll==0 and currents==0 are omitted.</summary>
        public static string BuildRyzenAdjArgs(CpuTuningValues v)
        {
            int stapm = Clamp(TuningParam.Stapm, v.StapmW);
            int fast  = Clamp(TuningParam.FastPpt, v.FastW);
            int slow  = Clamp(TuningParam.SlowPpt, v.SlowW);
            int tctl  = Clamp(TuningParam.Tctl, v.TctlC);
            string args = $"--stapm-limit={stapm * 1000} --fast-limit={fast * 1000} " +
                          $"--slow-limit={slow * 1000} --tctl-temp={tctl}";
            if (v.CoAll != 0)
            {
                int co = Clamp(TuningParam.CoAll, v.CoAll);
                int coEnc = co < 0 ? (0x100000 + co) : co;
                args += $" --set-coall={coEnc}";
            }
            if (v.TdcA > 0) args += $" --vrm-current={Clamp(TuningParam.Tdc, v.TdcA) * 1000}";
            if (v.EdcA > 0) args += $" --vrmmax-current={Clamp(TuningParam.Edc, v.EdcA) * 1000}";
            return args;
        }

        /// <summary>Parses ryzenadj stdout into per-line results. ryzenadj exits 0 even when a parameter is
        /// unsupported; failures appear as lines containing "not supported", "failed", or "error".</summary>
        public static IEnumerable<RyzenAdjResult> ParseRyzenAdjOutput(string stdout)
        {
            if (string.IsNullOrWhiteSpace(stdout)) yield break;
            foreach (string line in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                bool isFailure =
                    trimmed.IndexOf("not supported", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0;
                yield return new RyzenAdjResult(trimmed, !isFailure);
            }
        }

        /// <summary>Parses an nvidia-smi power-limit CSV row "min, current, max" (watts, nounits) into ints.
        /// Returns false if any field is missing/non-numeric (e.g. "N/A").</summary>
        public static bool ParseNvSmiPowerLimits(string csvRow, out int min, out int current, out int max)
        {
            min = current = max = 0;
            if (string.IsNullOrWhiteSpace(csvRow)) return false;
            string[] parts = csvRow.Split(',');
            if (parts.Length < 3) return false;
            if (!double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double dMin)) return false;
            if (!double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double dCur)) return false;
            if (!double.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double dMax)) return false;
            min = (int)System.Math.Round(dMin);
            current = (int)System.Math.Round(dCur);
            max = (int)System.Math.Round(dMax);
            return true;
        }
    }
}
