using System;
using System.Text.RegularExpressions;

namespace CoreCage.Core.Diagnostics
{
    /// <summary>Redacts personally-identifying substrings (user folder paths, account name, machine
    /// name) from any string before it could leave the machine — e.g. a crash report's
    /// exception text or stack-frame file paths. Pure + unit-testable; pass the identifiers in.</summary>
    public static class CrashScrubber
    {
        // C:\Users\<name>\...  ->  C:\Users\<user>\...   (also masks OTHER users' names in paths)
        private static readonly Regex WinUserPath =
            new Regex(@"([A-Za-z]:\\Users\\)[^\\\r\n]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // /Users/<name>/ or \Users\<name>\ variants on the forward-slash side
        private static readonly Regex NixUserPath =
            new Regex(@"(/Users/)[^/\r\n]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string? Scrub(string? text, string? userName, string? machineName)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string s = text!;
            s = WinUserPath.Replace(s, "$1<user>");
            s = NixUserPath.Replace(s, "$1<user>");
            if (!string.IsNullOrEmpty(userName))
                s = s.Replace(userName, "<user>", StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(machineName))
                s = s.Replace(machineName, "<machine>", StringComparison.OrdinalIgnoreCase);
            return s;
        }

        /// <summary>Convenience overload using the current environment's identifiers.</summary>
        public static string? Scrub(string? text) => Scrub(text, Environment.UserName, Environment.MachineName);
    }
}
