using System;
using System.Diagnostics;

namespace CoreCage.Core
{
    internal static class CommandRunner
    {
        internal static void RunCommand(string args, bool ignoreErrors = false)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c " + args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var process = Process.Start(psi);
                process?.WaitForExit(5000);
            }
            catch (Exception ex)
            {
                // Never let a failed helper command crash the app (esp. on a background thread, where an
                // uncaught throw is fatal). ignoreErrors now only gates logging, not whether we catch.
                if (!ignoreErrors) Logger.Log($"RunCommand warning: {ex.Message}");
            }
        }

        internal static void RunNetworkCommand(string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var process = Process.Start(psi);
                process?.WaitForExit(5000);
            }
            catch { }
        }

        internal static void RunPowerShell(string script)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"{script.Replace("\"", "\\\"")}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(10000);
            }
            catch (Exception ex)
            {
                Logger.Log($"RunPowerShell warning: {ex.Message}");
            }
        }
    }
}
