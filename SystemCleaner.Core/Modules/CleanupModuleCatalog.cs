using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SystemCleaner.Core.Abstractions;
using SystemCleaner.Core.Models;

namespace SystemCleaner.Core.Modules;

public static class CleanupModuleCatalog
{
    public static IReadOnlyList<ICleanupModule> CreateDefaultModules()
    {
        var modules = new List<ICleanupModule>
        {
            CreateTemporaryFilesModule(),
            CreateBrowserCacheModule(),
            CreateDiagnosticDataModule(),
            CreateLargeFileModule(),
            CreateDuplicateFilesModule()
        };

        return modules;
    }

    private static ICleanupModule CreateTemporaryFilesModule()
    {
        var rules = new List<DirectoryCleanupRule>();
        AddRuleIfUnique(rules, "User Temp", () => Path.GetTempPath());
        AddRuleIfUnique(rules, "Windows Temp", () => CombineSafe(GetWindowsDirectory(), "Temp"), requiresElevation: true);
        AddRuleIfUnique(rules, "Internet Cache", TryGetInternetCachePath);
        AddRuleIfUnique(rules, "IE Temp", () => CombineSafe(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "INetCache"));

        return new DirectoryCleanupModule(
            new CleanupModuleInfo("temp-files", "Temporary Files", "Removes temporary operating system and application data."),
            rules);
    }

    private static ICleanupModule CreateBrowserCacheModule()
    {
        var rules = new List<DirectoryCleanupRule>();
        AddBrowserRules(rules, "Microsoft Edge", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge", "User Data"));
        AddBrowserRules(rules, "Google Chrome", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "User Data"));
        AddFirefoxRules(rules);

        return new DirectoryCleanupModule(
            new CleanupModuleInfo("browser-cache", "Browser Cache", "Clears cache data for installed browsers."),
            rules);
    }

    private static ICleanupModule CreateLargeFileModule()
    {
        var rules = new List<LargeFileScanRule>
        {
            new("Downloads", () => CombineSafe(GetKnownFolder(Environment.SpecialFolder.UserProfile), "Downloads"), minimumSizeBytes: 250L * 1024 * 1024, maxResults: 50),
            new("Videos", () => GetKnownFolder(Environment.SpecialFolder.MyVideos), minimumSizeBytes: 500L * 1024 * 1024, maxResults: 40),
            new("Documents", () => GetKnownFolder(Environment.SpecialFolder.MyDocuments), minimumSizeBytes: 200L * 1024 * 1024, maxResults: 40),
            new("Desktop", () => GetKnownFolder(Environment.SpecialFolder.Desktop), minimumSizeBytes: 200L * 1024 * 1024, maxResults: 30)
        };

        return new LargeFileCleanupModule(
            new CleanupModuleInfo(
                "large-files",
                "Large Files",
                "Highlights oversized files in common folders so you can archive or remove them.",
                IsQuickCleanSafe: false,
                Warning: "Review each file before deleting—large media or installers may still be needed."),
            rules);
    }

    private static ICleanupModule CreateDuplicateFilesModule()
    {
        var rules = new List<DuplicateScanRule>
        {
            new("Downloads", () => CombineSafe(GetKnownFolder(Environment.SpecialFolder.UserProfile), "Downloads"), minimumSizeBytes: 20L * 1024 * 1024),
            new("Documents", () => GetKnownFolder(Environment.SpecialFolder.MyDocuments), minimumSizeBytes: 15L * 1024 * 1024),
            new("Pictures", () => GetKnownFolder(Environment.SpecialFolder.MyPictures), minimumSizeBytes: 10L * 1024 * 1024),
            new("Videos", () => GetKnownFolder(Environment.SpecialFolder.MyVideos), minimumSizeBytes: 20L * 1024 * 1024)
        };

        return new DuplicateCleanupModule(
            new CleanupModuleInfo(
                "duplicate-files",
                "Duplicate Files",
                "Finds duplicate files across common media and document folders for manual review.",
                IsQuickCleanSafe: false,
                Warning: "Only remove copies you are certain are safe to delete—originals are not automatically preserved."),
            rules);
    }

    private static ICleanupModule CreateDiagnosticDataModule()
    {
        var rules = new List<DirectoryCleanupRule>();
        AddRuleIfUnique(rules, "Crash Dumps", () => CombineSafe(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps"));
        AddRuleIfUnique(rules, "Windows Error Reporting Queue", () => CombineSafe(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "WER", "ReportQueue"));
        AddRuleIfUnique(rules, "Windows Error Reporting Archive", () => CombineSafe(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "WER", "ReportArchive"));
        AddRuleIfUnique(rules, "Minidump", () => CombineSafe(GetWindowsDirectory(), "Minidump"), requiresElevation: true);

        return new DirectoryCleanupModule(
            new CleanupModuleInfo("diagnostic-data", "Diagnostic Data", "Removes crash dumps and Windows error reporting data."),
            rules);
    }

    private static void AddBrowserRules(ICollection<DirectoryCleanupRule> rules, string browserName, string? basePath)
    {
        if (string.IsNullOrWhiteSpace(basePath) || !Directory.Exists(basePath))
        {
            return;
        }

        foreach (var profileDirectory in EnumerateDirectoriesSafe(basePath))
        {
            var cachePath = CombineSafe(profileDirectory, "Cache");
            if (cachePath is null)
            {
                continue;
            }

            var displayName = $"{browserName} ({Path.GetFileName(profileDirectory)})";
            AddRuleIfUnique(rules, displayName, () => cachePath);
        }
    }

    private static void AddFirefoxRules(ICollection<DirectoryCleanupRule> rules)
    {
        var profilesRoot = CombineSafe(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mozilla", "Firefox", "Profiles");
        if (profilesRoot is null || !Directory.Exists(profilesRoot))
        {
            return;
        }

        foreach (var profileDirectory in EnumerateDirectoriesSafe(profilesRoot))
        {
            var cachePath = CombineSafe(profileDirectory, "cache2");
            if (cachePath is null)
            {
                continue;
            }

            var displayName = $"Firefox ({Path.GetFileName(profileDirectory)})";
            AddRuleIfUnique(rules, displayName, () => cachePath);
        }
    }

    private static void AddRuleIfUnique(ICollection<DirectoryCleanupRule> rules, string displayName, Func<string?> pathResolver, bool requiresElevation = false)
    {
        var rule = new DirectoryCleanupRule(displayName, pathResolver, requiresElevation);
        if (rules.Any(existing => string.Equals(existing.DisplayName, displayName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        rules.Add(rule);
    }

    private static string? TryGetInternetCachePath()
    {
        try
        {
            var path = Environment.GetFolderPath(Environment.SpecialFolder.InternetCache);
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetWindowsDirectory()
    {
        try
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        }
        catch
        {
            return null;
        }
    }

    private static string? CombineSafe(string? first, params string[] others)
    {
        if (string.IsNullOrWhiteSpace(first))
        {
            return null;
        }

        try
        {
            return Path.Combine(new[] { first }.Concat(others).ToArray());
        }
        catch
        {
            return null;
        }
    }

    private static string? GetKnownFolder(Environment.SpecialFolder folder)
    {
        try
        {
            var path = Environment.GetFolderPath(folder);
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateDirectoriesSafe(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly).ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
