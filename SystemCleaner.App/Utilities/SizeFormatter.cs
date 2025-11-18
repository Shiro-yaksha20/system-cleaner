using System;

namespace SystemCleaner.App.Utilities;

public static class SizeFormatter
{
    private static readonly string[] Suffixes = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string FormatSize(long bytes)
    {
        if (bytes < 0)
        {
            bytes = 0;
        }

        if (bytes == 0)
        {
            return "0 B";
        }

        var magnitude = Math.Min(Suffixes.Length - 1, (int)Math.Log(bytes, 1024));
        var adjusted = bytes / Math.Pow(1024, magnitude);
        return $"{adjusted:0.##} {Suffixes[magnitude]}";
    }
}
