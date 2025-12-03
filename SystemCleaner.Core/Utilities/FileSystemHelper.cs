using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SystemCleaner.Core.Models;

namespace SystemCleaner.Core.Utilities;

internal static class FileSystemHelper
{
    private static readonly string[] RestrictedRoots = BuildRestrictedRoots();
    public static long CalculateDirectorySize(string path, CancellationToken cancellationToken, List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return 0;
        }

        var normalizedPath = NormalizePath(path);
        if (IsRestrictedPath(normalizedPath))
        {
            issues.Add($"Skipping restricted path '{normalizedPath}'.");
            return 0;
        }

        if (normalizedPath is null)
        {
            return 0;
        }

        var directory = new DirectoryInfo(normalizedPath);
        if (!directory.Exists)
        {
            return 0;
        }

        long totalSize = 0;
        var stack = new Stack<DirectoryInfo>();
        stack.Push(directory);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = stack.Pop();
            FileInfo[] files;

            try
            {
                files = current.GetFiles();
            }
            catch (Exception ex)
            {
                issues.Add($"Unable to enumerate files in '{current.FullName}': {ex.Message}");
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    totalSize += file.Length;
                }
                catch (Exception ex)
                {
                    issues.Add($"Unable to read '{file.FullName}': {ex.Message}");
                }
            }

            DirectoryInfo[] subDirectories;
            try
            {
                subDirectories = current.GetDirectories();
            }
            catch (Exception ex)
            {
                issues.Add($"Unable to enumerate directories in '{current.FullName}': {ex.Message}");
                continue;
            }

            foreach (var subDirectory in subDirectories)
            {
                stack.Push(subDirectory);
            }
        }

        return totalSize;
    }

    public static CleanupItemResult CleanDirectoryItem(CleanupItem item, CancellationToken cancellationToken)
    {
        var issues = new List<string>();
        long freedBytes = 0;

        var normalizedPath = NormalizePath(item.Path);
        if (IsRestrictedPath(normalizedPath))
        {
            issues.Add($"Skipping '{normalizedPath}' because it is a protected system location.");
            return new CleanupItemResult(item, succeeded: false, bytesFreed: 0, issues);
        }

        if (normalizedPath is null || !Directory.Exists(normalizedPath))
        {
            return new CleanupItemResult(item, succeeded: true, bytesFreed: 0, issues);
        }

        try
        {
            freedBytes = CleanDirectoryContents(new DirectoryInfo(normalizedPath), deleteRootDirectory: false, cancellationToken, issues);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            issues.Add($"Failed to clean '{item.Path}': {ex.Message}");
        }

        return new CleanupItemResult(item, issues.Count == 0, freedBytes, issues);
    }

    public static CleanupItemResult CleanFileItem(CleanupItem item)
    {
        var issues = new List<string>();

        var normalizedPath = NormalizePath(item.Path);
        if (IsRestrictedPath(normalizedPath))
        {
            issues.Add($"Skipping '{normalizedPath}' because it is a protected system file.");
            return new CleanupItemResult(item, succeeded: false, bytesFreed: 0, issues);
        }

        if (normalizedPath is null || !File.Exists(normalizedPath))
        {
            return new CleanupItemResult(item, succeeded: true, bytesFreed: 0, issues);
        }

        try
        {
            var fileInfo = new FileInfo(normalizedPath);
            var freedBytes = DeleteFileSafe(fileInfo, issues);
            return new CleanupItemResult(item, issues.Count == 0, freedBytes, issues);
        }
        catch (Exception ex)
        {
            issues.Add($"Failed to delete '{item.Path}': {ex.Message}");
            return new CleanupItemResult(item, succeeded: false, bytesFreed: 0, issues);
        }
    }

    private static long CleanDirectoryContents(DirectoryInfo directory, bool deleteRootDirectory, CancellationToken cancellationToken, List<string> issues)
    {
        long freedBytes = 0;

        FileInfo[] files;
        try
        {
            files = directory.GetFiles();
        }
        catch (Exception ex)
        {
            issues.Add($"Unable to enumerate files in '{directory.FullName}': {ex.Message}");
            files = Array.Empty<FileInfo>();
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            freedBytes += DeleteFileSafe(file, issues);
        }

        DirectoryInfo[] subDirectories;
        try
        {
            subDirectories = directory.GetDirectories();
        }
        catch (Exception ex)
        {
            issues.Add($"Unable to enumerate directories in '{directory.FullName}': {ex.Message}");
            subDirectories = Array.Empty<DirectoryInfo>();
        }

        foreach (var subDirectory in subDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsRestrictedPath(subDirectory.FullName))
            {
                issues.Add($"Skipping restricted path '{subDirectory.FullName}'.");
                continue;
            }

            if (IsReparsePoint(subDirectory))
            {
                issues.Add($"Skipping reparse point '{subDirectory.FullName}' to avoid deleting linked content.");
                continue;
            }
            freedBytes += CleanDirectoryContents(subDirectory, deleteRootDirectory: true, cancellationToken, issues);
        }

        if (deleteRootDirectory)
        {
            TryDeleteDirectory(directory, issues);
        }

        return freedBytes;
    }

    private static long DeleteFileSafe(FileInfo file, List<string> issues)
    {
        try
        {
            var length = file.Length;
            if ((file.Attributes & FileAttributes.ReadOnly) != 0)
            {
                file.Attributes = FileAttributes.Normal;
            }

            file.Delete();
            return length;
        }
        catch (Exception ex)
        {
            issues.Add($"Unable to delete file '{file.FullName}': {ex.Message}");
            return 0;
        }
    }

    private static void TryDeleteDirectory(DirectoryInfo directory, List<string> issues)
    {
        if (IsRestrictedPath(directory.FullName))
        {
            issues.Add($"Skipping restricted directory '{directory.FullName}'.");
            return;
        }

        if (IsReparsePoint(directory))
        {
            issues.Add($"Skipping reparse point '{directory.FullName}'.");
            return;
        }

        try
        {
            if ((directory.Attributes & FileAttributes.ReadOnly) != 0)
            {
                directory.Attributes = FileAttributes.Normal;
            }

            directory.Delete(recursive: true);
        }
        catch (Exception ex)
        {
            issues.Add($"Unable to delete directory '{directory.FullName}': {ex.Message}");
        }
    }

    private static bool IsRestrictedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return RestrictedRoots.Any(root => path.Equals(root, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private static string? NormalizePath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(rawPath)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return rawPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static string[] BuildRestrictedRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            var normalized = NormalizePath(candidate);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                roots.Add(normalized);
            }
        }

        Add(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.System));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86));
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows) ?? string.Empty, "WinSxS"));
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows) ?? string.Empty, "Installer"));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) ?? string.Empty, "WindowsApps"));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) ?? string.Empty, "WindowsApps"));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86));

        return roots.Where(root => !string.IsNullOrWhiteSpace(root)).ToArray();
    }

    private static bool IsReparsePoint(DirectoryInfo directory)
    {
        return (directory.Attributes & FileAttributes.ReparsePoint) != 0;
    }
}
