using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SyncWallpaper.Core;

namespace SyncWallpaper.Windows;

public sealed class LogService : IStage1Logger
{
    private const long MaxBytes = 1024 * 1024;
    private readonly string _directory;
    private readonly object _gate = new();
    private readonly IFaultInjector _faultInjector;
    public List<DiagnosticEvent> Recent { get; } = new();
    public LogService(DataPaths paths, IFaultInjector? faultInjector = null) { _directory = paths.Logs; _faultInjector = faultInjector ?? NoFaultInjector.Instance; Directory.CreateDirectory(_directory); }
    public void Write(string type, string message, string? profile = null, int? monitors = null, int? confidence = null)
    {
        var sanitized = Sanitize(message);
        var item = new DiagnosticEvent(DateTime.Now, type, sanitized, profile, monitors, confidence);
        lock (_gate)
        {
            Recent.Insert(0, item); if (Recent.Count > 300) Recent.RemoveRange(300, Recent.Count - 300);
            var file = Path.Combine(_directory, $"{DateTime.Now:yyyy-MM-dd}.log");
            try
            {
                _faultInjector.ThrowIfRequested(FaultPoint.LogUnwritable);
                File.AppendAllText(file, $"{item.Timestamp:HH:mm:ss} [{type}] {message}{Environment.NewLine}", Encoding.UTF8);
                if (new FileInfo(file).Length > MaxBytes) File.Move(file, file + ".1", true);
                foreach (var old in Directory.EnumerateFiles(_directory, "*.log*")) if (File.GetLastWriteTimeUtc(old) < DateTime.UtcNow.AddDays(-7)) File.Delete(old);
            }
            catch { }
        }
    }

    public void Info(string category, string message) => Write(category, message);
    public void Warn(string category, string message) => Write(category, "警告：" + message);
    public void Error(string category, string message) => Write(category, "错误：" + message);

    private static string Sanitize(string message)
        => Regex.Replace(message ?? string.Empty, @"(?i)([A-Z]:\\Users\\)[^\\\s]+", "$1<user>");
}
