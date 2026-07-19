using System;
using System.IO;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace CoreCage.Core
{
    /// <summary>
    /// Application logger. Serilog backs the file sink (structured, rolling daily, 7-day retention)
    /// while the public <see cref="Log"/>/<see cref="LogError"/> API and <see cref="UILogCallback"/>
    /// are preserved for back-compat. The legacy plain-text filename + "[HH:mm:ss] message" layout
    /// are reproduced exactly so existing log consumers/tooling keep working.
    /// </summary>
    public static class Logger
    {
        private static readonly Serilog.Core.Logger _log;

        /// <summary>
        /// Optional UI callback — set this to pipe log messages into a UI control.
        /// Called on whatever thread Log() is invoked from; caller must marshal to UI thread if needed.
        /// </summary>
        public static Action<string>? UILogCallback;

        /// <summary>Underlying Serilog logger — use for structured events and additional sinks.</summary>
        public static ILogger Sink => _log;

        static Logger()
        {
            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CoreCage", "Logs");

            Directory.CreateDirectory(logDir);

            // rollingInterval:Day turns "CoreCage_.log" into the legacy "CoreCage_yyyyMMdd.log"
            // name; retainedFileCountLimit:7 replaces the old manual 7-day cleanup. outputTemplate
            // reproduces the legacy "[HH:mm:ss] message" line (plus exception details on errors).
            _log = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext()
                .WriteTo.File(
                    path: Path.Combine(logDir, "CoreCage_.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    shared: true,
                    outputTemplate: "[{Timestamp:HH:mm:ss}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }

        public static void Log(string message)
        {
            try
            {
                _log.Information(message);
                UILogCallback?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
            }
            catch { }
        }

        public static void LogError(string message, Exception? ex = null)
        {
            try
            {
                if (ex != null) _log.Error(ex, message);
                else            _log.Error(message);

                string uiMsg = ex != null ? $"ERROR: {message} — {ex.Message}" : $"ERROR: {message}";
                UILogCallback?.Invoke($"[{DateTime.Now:HH:mm:ss}] {uiMsg}");
            }
            catch { }
        }

        /// <summary>
        /// Structured event for richer diagnostics. The template + property
        /// values are recorded structurally; the rendered text is mirrored to the UI callback.
        /// e.g. <c>Logger.Event("Applied {Preset} preset (CO {Offset})", "Gaming", -20)</c>.
        /// </summary>
        public static void Event(string messageTemplate, params object?[] propertyValues)
        {
            try
            {
                _log.Information(messageTemplate, propertyValues);
                if (UILogCallback != null)
                {
                    var rendered = new MessageTemplateParser()
                        .Parse(messageTemplate)
                        .Render(BuildProperties(messageTemplate, propertyValues));
                    UILogCallback($"[{DateTime.Now:HH:mm:ss}] {rendered}");
                }
            }
            catch { }
        }

        /// <summary>Flush + release the file sink. Call once on application exit.</summary>
        public static void Shutdown()
        {
            try { _log.Dispose(); } catch { }
        }

        private static System.Collections.Generic.IReadOnlyDictionary<string, LogEventPropertyValue> BuildProperties(
            string template, object?[] values)
        {
            var dict = new System.Collections.Generic.Dictionary<string, LogEventPropertyValue>();
            int i = 0;
            foreach (var tok in new MessageTemplateParser().Parse(template).Tokens)
            {
                if (tok is PropertyToken pt && i < values.Length)
                    dict[pt.PropertyName] = new ScalarValue(values[i++]);
            }
            return dict;
        }
    }
}
