using System;

namespace SystemCleaner.Core.Uninstall;

public enum ResidualItemKind
{
    Directory,
    File,
    RegistryKey,
    RegistryValue,
    Service,
    ScheduledTask,
    Driver
}

public sealed class ResidualItem
{
    private ResidualItem(string applicationName, string path, ResidualItemKind kind, string source, long? sizeBytes)
    {
        ApplicationName = applicationName ?? throw new ArgumentNullException(nameof(applicationName));
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Kind = kind;
        Source = source ?? string.Empty;
        SizeBytes = sizeBytes;
    }

    public string ApplicationName { get; }

    public string Path { get; }

    public ResidualItemKind Kind { get; }

    public string Source { get; }

    public long? SizeBytes { get; }

    public static ResidualItem Directory(string appName, string path, string source, long? sizeBytes) =>
        new(appName, path, ResidualItemKind.Directory, source, sizeBytes);

    public static ResidualItem File(string appName, string path, string source, long? sizeBytes) =>
        new(appName, path, ResidualItemKind.File, source, sizeBytes);

    public static ResidualItem RegistryKey(string appName, string path, string source) =>
        new(appName, path, ResidualItemKind.RegistryKey, source, sizeBytes: null);

    public static ResidualItem RegistryValue(string appName, string path, string source) =>
        new(appName, path, ResidualItemKind.RegistryValue, source, sizeBytes: null);

    public static ResidualItem Service(string appName, string serviceName, string source) =>
        new(appName, serviceName, ResidualItemKind.Service, source, sizeBytes: null);

    public static ResidualItem ScheduledTask(string appName, string taskPath, string source) =>
        new(appName, taskPath, ResidualItemKind.ScheduledTask, source, sizeBytes: null);

    public static ResidualItem Driver(string appName, string path, string source) =>
        new(appName, path, ResidualItemKind.Driver, source, sizeBytes: null);
}
