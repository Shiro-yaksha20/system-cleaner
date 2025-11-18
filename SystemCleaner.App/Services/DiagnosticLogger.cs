using System;
using System.IO;
using System.Text;

namespace SystemCleaner.App.Services;

internal static class DiagnosticLogger
{
    private static readonly object Sync = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SystemCleaner",
        "logs");

    public static string Log(Exception exception, string context)
    {
        if (exception is null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        var timestamp = DateTime.Now;
        var builder = new StringBuilder();
        builder.AppendLine(new string('-', 60));
        builder.AppendLine($"Timestamp: {timestamp:O}");
        builder.AppendLine($"Context: {context}");
        builder.AppendLine("Exception:");
        builder.AppendLine(exception.ToString());
        return Write(builder.ToString(), timestamp);
    }

    public static string LogInfo(string message, string context)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var timestamp = DateTime.Now;
        var builder = new StringBuilder();
        builder.AppendLine(new string('-', 60));
        builder.AppendLine($"Timestamp: {timestamp:O}");
        builder.AppendLine($"Context: {context}");
        builder.AppendLine($"Message: {message}");
        return Write(builder.ToString(), timestamp);
    }

    private static string Write(string content, DateTime timestamp)
    {
        var logPath = Path.Combine(LogDirectory, $"system-cleaner-{timestamp:yyyyMMdd}.log");

        try
        {
            Directory.CreateDirectory(LogDirectory);

            lock (Sync)
            {
                File.AppendAllText(logPath, content);
            }
        }
        catch
        {
            return string.Empty;
        }

        return logPath;
    }
}
