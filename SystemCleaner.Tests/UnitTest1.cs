using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SystemCleaner.Core.Models;
using SystemCleaner.Core.Modules;

namespace SystemCleaner.Tests;

public sealed class DirectoryCleanupModuleTests : IDisposable
{
    private readonly string _rootPath;

    public DirectoryCleanupModuleTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), $"SystemCleanerTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootPath);
    }

    [Fact]
    public async Task ScanAndClean_RemovesFilesAndReportsAccurateSize()
    {
        var module = CreateModule();
        var file1 = CreateFile("file1.tmp", 1024);
        var file2 = CreateFile(Path.Combine("nested", "file2.log"), 2048);
        _ = file1;
        _ = file2;

        var scanResult = await module.ScanAsync();

        Assert.Single(scanResult.Items);
        Assert.Equal(3_072, scanResult.TotalSizeBytes);

        var cleanResult = await module.CleanAsync(scanResult.Items);

        Assert.Single(cleanResult.ItemResults);
        Assert.True(cleanResult.ItemResults[0].Succeeded);
        Assert.Equal(scanResult.TotalSizeBytes, cleanResult.BytesFreed);
        Assert.False(Directory.EnumerateFileSystemEntries(_rootPath).Any());
    }

    private DirectoryCleanupModule CreateModule()
    {
        var info = new CleanupModuleInfo("test", "Test Module", "Test description");
        var rule = new DirectoryCleanupRule("Test Target", () => _rootPath);
        return new DirectoryCleanupModule(info, new[] { rule });
    }

    private string CreateFile(string relativePath, int sizeBytes)
    {
        var fullPath = Path.Combine(_rootPath, relativePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory!);
        }

        var buffer = new byte[sizeBytes];
        Random.Shared.NextBytes(buffer);
        File.WriteAllBytes(fullPath, buffer);
        return fullPath;
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            try
            {
                Directory.Delete(_rootPath, recursive: true);
            }
            catch
            {
                // Swallow cleanup exceptions in test teardown.
            }
        }
    }
}
