using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SystemCleaner.Core.Models;

namespace SystemCleaner.Core.Utilities;

internal static class FileSystemHelper
{
    public static long CalculateDirectorySize(string path, CancellationToken cancellationToken, List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return 0;
        }

        var directory = new DirectoryInfo(path);
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

        if (!Directory.Exists(item.Path))
        {
            return new CleanupItemResult(item, succeeded: true, bytesFreed: 0, issues);
        }

        try
        {
            freedBytes = CleanDirectoryContents(new DirectoryInfo(item.Path), deleteRootDirectory: false, cancellationToken, issues);
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

        if (!File.Exists(item.Path))
        {
            return new CleanupItemResult(item, succeeded: true, bytesFreed: 0, issues);
        }

        try
        {
            var fileInfo = new FileInfo(item.Path);
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
}
