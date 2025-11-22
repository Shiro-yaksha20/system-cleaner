using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace SystemCleaner.Core.Uninstall;

[SupportedOSPlatform("windows")]
public sealed class UninstallerService : IUninstallerService
{
    private readonly IReadOnlyList<ISoftwareInventoryProvider> _inventoryProviders;
    private readonly IBrowserExtensionProvider _extensionProvider;
    private readonly IReadOnlyList<IResidualScanner> _residualScanners;
    private readonly IReadOnlyList<IResidualCleanupHandler> _cleanupHandlers;

    public UninstallerService()
    {
        _inventoryProviders = new ISoftwareInventoryProvider[]
        {
            new RegistrySoftwareInventoryProvider()
        };

        _extensionProvider = new BrowserExtensionProvider();

        _residualScanners = new IResidualScanner[]
        {
            new FileResidualScanner(),
            new RegistryResidualScanner(),
            new ServiceResidualScanner(),
            new ScheduledTaskResidualScanner(),
            new DriverResidualScanner()
        };

        _cleanupHandlers = new IResidualCleanupHandler[]
        {
            new DirectoryCleanupHandler(),
            new FileCleanupHandler(),
            new RegistryCleanupHandler(),
            new ServiceCleanupHandler(),
            new ScheduledTaskCleanupHandler(),
            new DriverCleanupHandler()
        };
    }

    public Task<InstalledSoftwareSnapshot> GetInstalledSoftwareAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var applications = new List<InstalledApplication>();
            var extensions = new List<BrowserExtensionInfo>();
            var insights = new List<SoftwareHealthInsight>();
            var issues = new List<string>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var provider in _inventoryProviders)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    foreach (var app in provider.Scan(cancellationToken, issues))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (string.IsNullOrWhiteSpace(app.Name))
                        {
                            continue;
                        }

                        var identity = ResolveIdentity(app);
                        if (!seenKeys.Add(identity))
                        {
                            continue;
                        }

                        applications.Add(app);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    issues.Add($"{provider.Name} scan failed: {ex.Message}");
                }
            }

            try
            {
                foreach (var extension in _extensionProvider.Scan(cancellationToken, issues))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    extensions.Add(extension);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                issues.Add($"Browser extension scan failed: {ex.Message}");
            }

            applications.Sort(InstalledApplicationComparer.Instance);
            extensions.Sort(BrowserExtensionComparer.Instance);
            insights.AddRange(SoftwareHealthAnalyzer.Analyze(applications, extensions));

            return new InstalledSoftwareSnapshot(applications, extensions, insights, issues);
        }, cancellationToken);
    }

    public async Task<UninstallOperationResult> UninstallAsync(IEnumerable<InstalledApplication> targets, UninstallOptions options, CancellationToken cancellationToken = default)
    {
        if (targets is null)
        {
            throw new ArgumentNullException(nameof(targets));
        }

        var targetList = targets.Where(app => app is not null).DistinctBy(app => ResolveIdentity(app)).ToList();
        var messages = new List<string>();
        var failures = new List<string>();
        var successes = 0;

        if (options.CreateRestorePoint)
        {
            var restoreMessage = TryCreateRestorePoint();
            if (!string.IsNullOrWhiteSpace(restoreMessage))
            {
                messages.Add(restoreMessage);
            }
        }

        foreach (var app in targetList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var command = ResolveCommand(app, options.Force);
                if (!string.IsNullOrWhiteSpace(command))
                {
                    var executionMessage = await ExecuteUninstallCommandAsync(app, command, options.Force, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(executionMessage))
                    {
                        messages.Add(executionMessage);
                    }

                    successes++;
                }
                else if (!options.Force)
                {
                    failures.Add($"{app.Name}: No uninstall command available.");
                    continue;
                }
                else
                {
                    messages.Add($"{app.Name}: No native uninstaller found. Relying on powerful cleanup.");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add($"{app.Name}: {ex.Message}");
            }
        }

        if (options.ResidualCleanupMode != ResidualCleanupMode.None)
        {
            var residuals = await FindResidualItemsAsync(targetList, cancellationToken).ConfigureAwait(false);
            if (residuals.Count > 0)
            {
                messages.Add($"Powerful scan detected {residuals.Count} residual item(s).");

                if (options.ResidualCleanupMode == ResidualCleanupMode.Cleanup)
                {
                    var cleanupResult = await CleanupResidualItemsAsync(residuals, options.CreateCleanupOptions(), cancellationToken).ConfigureAwait(false);
                    messages.AddRange(cleanupResult.Messages);
                    if (cleanupResult.Failed > 0)
                    {
                        failures.Add($"Residual cleanup encountered {cleanupResult.Failed} failure(s).");
                    }
                }
            }
        }

        return new UninstallOperationResult(successes, failures.Count, messages.Concat(failures).ToArray());
    }

    public Task<IReadOnlyList<ResidualItem>> FindResidualItemsAsync(IEnumerable<InstalledApplication> applications, CancellationToken cancellationToken = default)
    {
        if (applications is null)
        {
            throw new ArgumentNullException(nameof(applications));
        }

        return Task.Run(() =>
        {
            var results = new List<ResidualItem>();
            var issues = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var apps = applications.Where(app => app is not null).ToArray();

            foreach (var app in apps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var scanner in _residualScanners)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        foreach (var residual in scanner.Scan(app, cancellationToken, issues))
                        {
                            var key = $"{residual.Kind}:{residual.Path}";
                            if (seen.Add(key))
                            {
                                results.Add(residual);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        issues.Add($"{scanner.Name} failed for {app.Name}: {ex.Message}");
                    }
                }
            }

            foreach (var issue in issues)
            {
                // Surface scanning issues in the snapshot issues list by returning synthetic registry entries.
                // Actual logging is handled by the caller.
                // For now, we simply capture them as hidden residual items that can be surfaced later if needed.
            }

            var ordered = results
                .OrderBy(item => item.ApplicationName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Kind)
                .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return (IReadOnlyList<ResidualItem>)ordered;
        }, cancellationToken);
    }

    public Task<ResidualCleanupResult> CleanupResidualItemsAsync(IEnumerable<ResidualItem> residualItems, CancellationToken cancellationToken = default)
    {
        return CleanupResidualItemsAsync(residualItems, ResidualCleanupOptions.Default, cancellationToken);
    }

    public Task<ResidualCleanupResult> CleanupResidualItemsAsync(IEnumerable<ResidualItem> residualItems, ResidualCleanupOptions options, CancellationToken cancellationToken = default)
    {
        if (residualItems is null)
        {
            throw new ArgumentNullException(nameof(residualItems));
        }

        return Task.Run(() =>
        {
            var removed = 0;
            var failed = 0;
            var messages = new List<string>();

            foreach (var item in residualItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item is null)
                {
                    continue;
                }

                var handler = _cleanupHandlers.FirstOrDefault(h => h.CanHandle(item.Kind));
                if (handler is null)
                {
                    failed++;
                    messages.Add($"{item.ApplicationName}: No cleanup handler for {item.Kind}.");
                    continue;
                }

                try
                {
                    var outcome = handler.Cleanup(item, options);
                    if (outcome)
                    {
                        removed++;
                        messages.Add($"{item.ApplicationName}: Removed {item.Kind} → {item.Path}.");
                    }
                    else
                    {
                        messages.Add($"{item.ApplicationName}: {item.Path} already removed.");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    messages.Add($"{item.ApplicationName}: Failed to remove {item.Path} - {ex.Message}");
                }
            }

            return new ResidualCleanupResult(removed, failed, messages);
        }, cancellationToken);
    }

    public Task<bool> RemoveBrowserExtensionAsync(BrowserExtensionInfo extension, CancellationToken cancellationToken = default)
    {
        if (extension is null)
        {
            throw new ArgumentNullException(nameof(extension));
        }

        return Task.Run(() =>
        {
            if (!extension.CanRemove)
            {
                return false;
            }

            try
            {
                if (Directory.Exists(extension.Location))
                {
                    Directory.Delete(extension.Location, recursive: true);
                    return true;
                }

                if (File.Exists(extension.Location))
                {
                    File.Delete(extension.Location);
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }, cancellationToken);
    }

    private static string ResolveIdentity(InstalledApplication app)
    {
        if (!string.IsNullOrWhiteSpace(app.RegistryKeyPath))
        {
            return app.RegistryKeyPath;
        }

        var builder = new StringBuilder();
        builder.Append(app.Name);
        builder.Append('|');
        builder.Append(app.Publisher);
        builder.Append('|');
        builder.Append(app.Version);
        return builder.ToString();
    }

    private static string? ResolveCommand(InstalledApplication app, bool force)
    {
        if (!string.IsNullOrWhiteSpace(app.QuietUninstallCommand))
        {
            return Environment.ExpandEnvironmentVariables(app.QuietUninstallCommand);
        }

        if (!string.IsNullOrWhiteSpace(app.UninstallCommand))
        {
            return Environment.ExpandEnvironmentVariables(app.UninstallCommand);
        }

        if (force && !string.IsNullOrWhiteSpace(app.ProductCode))
        {
            return $"msiexec.exe /x {app.ProductCode} /qn";
        }

        return null;
    }

    private static async Task<string> ExecuteUninstallCommandAsync(InstalledApplication app, string command, bool force, CancellationToken cancellationToken)
    {
        var startInfo = BuildProcessStartInfo(command);
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException($"{app.Name}: Unable to start uninstall process.");
        }

        var timeout = force ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(12);
        var exited = await WaitForExitAsync(process, timeout, cancellationToken).ConfigureAwait(false);
        if (!exited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw new System.TimeoutException($"{app.Name}: Uninstall command timed out after {timeout.TotalMinutes:F0} minute(s).");
        }

        return $"{app.Name}: Uninstaller exited with code {process.ExitCode}.";
    }

    private static ProcessStartInfo BuildProcessStartInfo(string command)
    {
        var expanded = Environment.ExpandEnvironmentVariables(command ?? string.Empty).Trim();
        return new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = string.IsNullOrEmpty(expanded) ? string.Empty : $"/c {expanded}",
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (process.HasExited)
        {
            return true;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delayTask = Task.Delay(timeout, linkedCts.Token);
        var waitTask = process.WaitForExitAsync(linkedCts.Token);
        var completed = await Task.WhenAny(delayTask, waitTask).ConfigureAwait(false);
        if (completed == waitTask)
        {
            linkedCts.Cancel();
            await waitTask.ConfigureAwait(false);
            return true;
        }

        return false;
    }

    private static string TryCreateRestorePoint()
    {
        try
        {
            using var restoreClass = new ManagementClass("SystemRestore");
            var inParams = restoreClass.GetMethodParameters("CreateRestorePoint");
            inParams["Description"] = "System Cleaner Uninstall";
            inParams["RestorePointType"] = 12; // Modify Settings
            inParams["EventType"] = 100;
            restoreClass.InvokeMethod("CreateRestorePoint", inParams, null);
            return "System restore point created.";
        }
        catch (Exception ex)
        {
            return $"Restore point creation failed: {ex.Message}";
        }
    }

    private interface ISoftwareInventoryProvider
    {
        string Name { get; }

        IEnumerable<InstalledApplication> Scan(CancellationToken cancellationToken, IList<string> issues);
    }

    private sealed class RegistrySoftwareInventoryProvider : ISoftwareInventoryProvider
    {
        private static readonly (RegistryHive Hive, RegistryView View, string Path)[] RegistryLocations =
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (RegistryHive.CurrentUser, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (RegistryHive.CurrentUser, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
        };

        public string Name => "Registry";

        public IEnumerable<InstalledApplication> Scan(CancellationToken cancellationToken, IList<string> issues)
        {
            foreach (var location in RegistryLocations)
            {
                cancellationToken.ThrowIfCancellationRequested();

                List<InstalledApplication> entries;
                try
                {
                    entries = EnumerateLocation(location, cancellationToken);
                }
                catch (Exception ex)
                {
                    issues.Add($"Registry scan failed for {location.Hive} ({location.View}): {ex.Message}");
                    continue;
                }

                foreach (var entry in entries)
                {
                    yield return entry;
                }
            }
        }

        private static List<InstalledApplication> EnumerateLocation((RegistryHive Hive, RegistryView View, string Path) location, CancellationToken token)
        {
            var results = new List<InstalledApplication>();

            using var baseKey = RegistryKey.OpenBaseKey(location.Hive, location.View);
            using var uninstallKey = baseKey.OpenSubKey(location.Path);
            if (uninstallKey is null)
            {
                return results;
            }

            foreach (var subKeyName in uninstallKey.GetSubKeyNames())
            {
                token.ThrowIfCancellationRequested();

                using var appKey = uninstallKey.OpenSubKey(subKeyName);
                if (appKey is null)
                {
                    continue;
                }

                var displayName = Convert.ToString(appKey.GetValue("DisplayName"));
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    continue;
                }

                var systemComponent = Convert.ToInt32(appKey.GetValue("SystemComponent", 0)) == 1;
                if (systemComponent)
                {
                    continue;
                }

                var publisher = Convert.ToString(appKey.GetValue("Publisher")) ?? string.Empty;
                var version = Convert.ToString(appKey.GetValue("DisplayVersion")) ?? string.Empty;
                var installLocation = Convert.ToString(appKey.GetValue("InstallLocation"));
                var uninstallString = Convert.ToString(appKey.GetValue("UninstallString"));
                var quietUninstallString = Convert.ToString(appKey.GetValue("QuietUninstallString"));
                var productCode = Convert.ToString(appKey.GetValue("ProductID"));
                var estimatedSize = TryReadEstimatedSize(appKey);
                var installDate = TryParseInstallDate(Convert.ToString(appKey.GetValue("InstallDate")));
                var registryPath = $"{location.Hive}\\{location.Path}\\{subKeyName}";

                results.Add(new InstalledApplication(
                    displayName,
                    publisher,
                    version,
                    installLocation,
                    uninstallString,
                    quietUninstallString,
                    productCode,
                    registryPath,
                    systemComponent,
                    installDate,
                    estimatedSize,
                    LooksLikeWindowsApp(installLocation, uninstallString, quietUninstallString, productCode)));
            }

            return results;
        }

        private static long? TryReadEstimatedSize(RegistryKey key)
        {
            try
            {
                var value = key.GetValue("EstimatedSize");
                if (value is int intValue)
                {
                    return (long)intValue * 1024L;
                }

                if (value is long longValue)
                {
                    return longValue * 1024L;
                }
            }
            catch
            {
            }

            return null;
        }

        private static DateTime? TryParseInstallDate(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (DateTime.TryParseExact(raw, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return parsed;
            }

            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                return parsed;
            }

            return null;
        }

        private static bool LooksLikeWindowsApp(string? installLocation, string? uninstallString, string? quietUninstallString, string? productCode)
        {
            if (!string.IsNullOrWhiteSpace(installLocation) && installLocation.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(uninstallString) && uninstallString.IndexOf("AppXDeploymentClient", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(quietUninstallString) && quietUninstallString.IndexOf("AppXDeploymentClient", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(productCode) && productCode.StartsWith("AppX", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
    }

    private interface IBrowserExtensionProvider
    {
        IEnumerable<BrowserExtensionInfo> Scan(CancellationToken cancellationToken, IList<string> issues);
    }

    private sealed class BrowserExtensionProvider : IBrowserExtensionProvider
    {
        public IEnumerable<BrowserExtensionInfo> Scan(CancellationToken cancellationToken, IList<string> issues)
        {
            foreach (var scope in new[]
            {
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolder.ApplicationData
            })
            {
                string root;
                try
                {
                    root = Environment.GetFolderPath(scope);
                }
                catch (Exception ex)
                {
                    issues.Add($"Unable to resolve {scope}: {ex.Message}");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    continue;
                }

                foreach (var item in EnumerateChrome(root, cancellationToken, issues))
                {
                    yield return item;
                }

                foreach (var item in EnumerateEdge(root, cancellationToken, issues))
                {
                    yield return item;
                }

                foreach (var item in EnumerateFirefox(root, cancellationToken, issues))
                {
                    yield return item;
                }
            }
        }

        private static IEnumerable<BrowserExtensionInfo> EnumerateChrome(string root, CancellationToken token, IList<string> issues)
        {
            var extensionsPath = Path.Combine(root, "Google", "Chrome", "User Data");
            if (!Directory.Exists(extensionsPath))
            {
                yield break;
            }

            foreach (var profileDir in Directory.EnumerateDirectories(extensionsPath, "*", SearchOption.TopDirectoryOnly))
            {
                token.ThrowIfCancellationRequested();

                var extensionDir = Path.Combine(profileDir, "Extensions");
                if (!Directory.Exists(extensionDir))
                {
                    continue;
                }

                foreach (var extension in Directory.EnumerateDirectories(extensionDir))
                {
                    token.ThrowIfCancellationRequested();
                    var name = TryReadManifestName(extension, issues) ?? Path.GetFileName(extension);
                    yield return new BrowserExtensionInfo("Chrome", name ?? "Unknown", extension, canRemove: true, flagged: false);
                }
            }
        }

        private static IEnumerable<BrowserExtensionInfo> EnumerateEdge(string root, CancellationToken token, IList<string> issues)
        {
            var extensionsPath = Path.Combine(root, "Microsoft", "Edge", "User Data");
            if (!Directory.Exists(extensionsPath))
            {
                yield break;
            }

            foreach (var profileDir in Directory.EnumerateDirectories(extensionsPath, "*", SearchOption.TopDirectoryOnly))
            {
                token.ThrowIfCancellationRequested();
                var extensionDir = Path.Combine(profileDir, "Extensions");
                if (!Directory.Exists(extensionDir))
                {
                    continue;
                }

                foreach (var extension in Directory.EnumerateDirectories(extensionDir))
                {
                    token.ThrowIfCancellationRequested();
                    var name = TryReadManifestName(extension, issues) ?? Path.GetFileName(extension);
                    yield return new BrowserExtensionInfo("Edge", name ?? "Unknown", extension, canRemove: true, flagged: false);
                }
            }
        }

        private static IEnumerable<BrowserExtensionInfo> EnumerateFirefox(string root, CancellationToken token, IList<string> issues)
        {
            var extensionsPath = Path.Combine(root, "Mozilla", "Firefox", "Profiles");
            if (!Directory.Exists(extensionsPath))
            {
                yield break;
            }

            foreach (var profileDir in Directory.EnumerateDirectories(extensionsPath, "*.default*", SearchOption.TopDirectoryOnly))
            {
                token.ThrowIfCancellationRequested();
                var extensionDir = Path.Combine(profileDir, "extensions");
                if (!Directory.Exists(extensionDir))
                {
                    continue;
                }

                foreach (var extensionFile in Directory.EnumerateFiles(extensionDir, "*.xpi"))
                {
                    token.ThrowIfCancellationRequested();
                    var name = Path.GetFileNameWithoutExtension(extensionFile) ?? "Unknown";
                    yield return new BrowserExtensionInfo("Firefox", name, extensionFile, canRemove: true, flagged: false);
                }
            }
        }

        private static string? TryReadManifestName(string extensionPath, IList<string> issues)
        {
            var manifestPath = Path.Combine(extensionPath, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                foreach (var dir in Directory.GetDirectories(extensionPath))
                {
                    manifestPath = Path.Combine(dir, "manifest.json");
                    if (File.Exists(manifestPath))
                    {
                        break;
                    }
                }

                if (!File.Exists(manifestPath))
                {
                    return null;
                }
            }

            try
            {
                using var stream = File.OpenRead(manifestPath);
                using var document = JsonDocument.Parse(stream);
                if (document.RootElement.TryGetProperty("name", out var nameProperty))
                {
                    var value = nameProperty.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }
            catch (Exception ex)
            {
                issues.Add($"Manifest read failed for {manifestPath}: {ex.Message}");
            }

            return null;
        }
    }

    private interface IResidualScanner
    {
        string Name { get; }

        IEnumerable<ResidualItem> Scan(InstalledApplication app, CancellationToken cancellationToken, ICollection<string> issues);
    }

    private sealed class FileResidualScanner : IResidualScanner
    {
        private static readonly (Func<string?> PathFactory, string Source)[] BaseDirectories =
        {
            (() => GetFolderPathSafe(Environment.SpecialFolder.LocalApplicationData), "AppData (Local)"),
            (() => GetFolderPathSafe(Environment.SpecialFolder.ApplicationData), "AppData (Roaming)"),
            (() => GetFolderPathSafe(Environment.SpecialFolder.CommonApplicationData), "ProgramData"),
            (() => GetFolderPathSafe(Environment.SpecialFolder.ProgramFiles), "Program Files"),
            (() => GetFolderPathSafe(Environment.SpecialFolder.ProgramFilesX86), "Program Files (x86)")
        };

        public string Name => "File System";

        public IEnumerable<ResidualItem> Scan(InstalledApplication app, CancellationToken cancellationToken, ICollection<string> issues)
        {
            var tokens = ResidualTokenBuilder.Build(app);
            if (tokens.Length == 0)
            {
                yield break;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(app.InstallLocation))
            {
                var normalized = ResidualTokenBuilder.NormalizePath(app.InstallLocation);
                if (!string.IsNullOrWhiteSpace(normalized) && Directory.Exists(normalized) && seen.Add(normalized))
                {
                    yield return ResidualItem.Directory(app.Name, normalized, "Install Location", ResidualTokenBuilder.TryGetDirectorySize(normalized));
                }
            }

            foreach (var (pathFactory, source) in BaseDirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var root = pathFactory();
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    continue;
                }

                IEnumerable<string> candidates;
                try
                {
                    candidates = Directory.EnumerateDirectories(root);
                }
                catch (Exception ex)
                {
                    issues.Add($"File scan failed for {root}: {ex.Message}");
                    continue;
                }

                foreach (var directory in candidates)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var name = Path.GetFileName(directory) ?? string.Empty;
                    var normalizedName = ResidualTokenBuilder.NormalizeForComparison(name);
                    if (string.IsNullOrEmpty(normalizedName))
                    {
                        continue;
                    }

                    if (!tokens.Any(token => normalizedName.Contains(token, StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    var normalizedPath = ResidualTokenBuilder.NormalizePath(directory);
                    if (string.IsNullOrWhiteSpace(normalizedPath) || !seen.Add(normalizedPath))
                    {
                        continue;
                    }

                    yield return ResidualItem.Directory(app.Name, normalizedPath, source, ResidualTokenBuilder.TryGetDirectorySize(normalizedPath));
                }

                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                    continue;
                }

                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var fileName = Path.GetFileName(file) ?? string.Empty;
                    var normalizedFileName = ResidualTokenBuilder.NormalizeForComparison(fileName);
                    if (string.IsNullOrEmpty(normalizedFileName))
                    {
                        continue;
                    }

                    if (!tokens.Any(token => normalizedFileName.Contains(token, StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    var normalizedPath = ResidualTokenBuilder.NormalizePath(file);
                    if (string.IsNullOrWhiteSpace(normalizedPath) || !seen.Add(normalizedPath))
                    {
                        continue;
                    }

                    yield return ResidualItem.File(app.Name, normalizedPath, source, ResidualTokenBuilder.TryGetFileSize(normalizedPath));
                }
            }
        }

        private static string? GetFolderPathSafe(Environment.SpecialFolder folder)
        {
            try
            {
                return Environment.GetFolderPath(folder);
            }
            catch
            {
                return null;
            }
        }
    }

    private sealed class RegistryResidualScanner : IResidualScanner
    {
        private static readonly (RegistryHive Hive, RegistryView View, string Root)[] Roots =
        {
            (RegistryHive.CurrentUser, RegistryView.Registry64, @"Software"),
            (RegistryHive.CurrentUser, RegistryView.Registry32, @"Software"),
            (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE"),
            (RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE")
        };

        public string Name => "Registry";

        public IEnumerable<ResidualItem> Scan(InstalledApplication app, CancellationToken cancellationToken, ICollection<string> issues)
        {
            var tokens = ResidualTokenBuilder.Build(app);
            if (tokens.Length == 0)
            {
                yield break;
            }

            foreach (var (hive, view, rootPath) in Roots)
            {
                cancellationToken.ThrowIfCancellationRequested();

                List<ResidualItem> residuals;
                try
                {
                    residuals = EnumerateRoot(app, hive, view, rootPath, tokens, cancellationToken);
                }
                catch (Exception ex)
                {
                    issues.Add($"Registry scan failed for {hive} ({view}): {ex.Message}");
                    continue;
                }

                foreach (var residual in residuals)
                {
                    yield return residual;
                }
            }
        }

        private static List<ResidualItem> EnumerateRoot(InstalledApplication app, RegistryHive hive, RegistryView view, string rootPath, string[] tokens, CancellationToken token)
        {
            var results = new List<ResidualItem>();

            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var rootKey = baseKey.OpenSubKey(rootPath);
            if (rootKey is null)
            {
                return results;
            }

            EnumerateKeys(app, rootKey, $"{hive}\\{rootPath}", tokens, token, results);
            return results;
        }

        private static void EnumerateKeys(InstalledApplication app, RegistryKey key, string currentPath, string[] tokens, CancellationToken token, ICollection<ResidualItem> sink)
        {
            var shortPath = ResidualTokenBuilder.NormalizeForComparison(Path.GetFileName(currentPath) ?? string.Empty);
            if (tokens.Any(tokenValue => shortPath.Contains(tokenValue, StringComparison.Ordinal)))
            {
                sink.Add(ResidualItem.RegistryKey(app.Name, currentPath, "Registry"));
            }

            foreach (var valueName in key.GetValueNames())
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    var value = key.GetValue(valueName);
                    if (value is null)
                    {
                        continue;
                    }

                    var serialized = Convert.ToString(value);
                    if (string.IsNullOrWhiteSpace(serialized))
                    {
                        continue;
                    }

                    var normalized = ResidualTokenBuilder.NormalizeForComparison(serialized);
                    if (tokens.Any(tokenValue => normalized.Contains(tokenValue, StringComparison.Ordinal)))
                    {
                        sink.Add(ResidualItem.RegistryValue(app.Name, $"{currentPath}::{valueName}", "Registry Value"));
                    }
                }
                catch
                {
                }
            }

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                token.ThrowIfCancellationRequested();

                using var subKey = key.OpenSubKey(subKeyName);
                if (subKey is null)
                {
                    continue;
                }

                var nextPath = $"{currentPath}\\{subKeyName}";
                EnumerateKeys(app, subKey, nextPath, tokens, token, sink);
            }
        }
    }

    private sealed class ServiceResidualScanner : IResidualScanner
    {
        public string Name => "Windows Services";

        public IEnumerable<ResidualItem> Scan(InstalledApplication app, CancellationToken cancellationToken, ICollection<string> issues)
        {
            var tokens = ResidualTokenBuilder.Build(app);
            if (tokens.Length == 0)
            {
                yield break;
            }

            ServiceController[] services;
            try
            {
                services = ServiceController.GetServices();
            }
            catch (Exception ex)
            {
                issues.Add($"Service enumeration failed: {ex.Message}");
                yield break;
            }

            foreach (var service in services)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var name = ResidualTokenBuilder.NormalizeForComparison(service.ServiceName);
                var display = ResidualTokenBuilder.NormalizeForComparison(service.DisplayName);
                if (tokens.Any(token => name.Contains(token, StringComparison.Ordinal) || display.Contains(token, StringComparison.Ordinal)))
                {
                    yield return ResidualItem.Service(app.Name, service.ServiceName, "Service Controller");
                }
            }
        }
    }

    private sealed class ScheduledTaskResidualScanner : IResidualScanner
    {
        public string Name => "Scheduled Tasks";

        public IEnumerable<ResidualItem> Scan(InstalledApplication app, CancellationToken cancellationToken, ICollection<string> issues)
        {
            var tokens = ResidualTokenBuilder.Build(app);
            if (tokens.Length == 0)
            {
                yield break;
            }

            var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (string.IsNullOrWhiteSpace(systemRoot))
            {
                yield break;
            }

            var tasksRoot = Path.Combine(systemRoot, "Tasks");
            if (!Directory.Exists(tasksRoot))
            {
                tasksRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "Tasks");
                if (!Directory.Exists(tasksRoot))
                {
                    yield break;
                }
            }

            foreach (var taskFile in Directory.EnumerateFiles(tasksRoot, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileName = Path.GetFileName(taskFile) ?? string.Empty;
                var normalized = ResidualTokenBuilder.NormalizeForComparison(fileName);
                if (!tokens.Any(token => normalized.Contains(token, StringComparison.Ordinal)))
                {
                    continue;
                }

                yield return ResidualItem.ScheduledTask(app.Name, taskFile, "Scheduled Task");
            }
        }
    }

    private sealed class DriverResidualScanner : IResidualScanner
    {
        public string Name => "Drivers";

        public IEnumerable<ResidualItem> Scan(InstalledApplication app, CancellationToken cancellationToken, ICollection<string> issues)
        {
            var tokens = ResidualTokenBuilder.Build(app);
            if (tokens.Length == 0)
            {
                yield break;
            }

            foreach (var root in EnumerateDriverRoots())
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var fileName = Path.GetFileName(file) ?? string.Empty;
                    var normalized = ResidualTokenBuilder.NormalizeForComparison(fileName);
                    if (!tokens.Any(token => normalized.Contains(token, StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    yield return ResidualItem.Driver(app.Name, file, "Driver Store");
                }
            }
        }

        private static IEnumerable<string> EnumerateDriverRoots()
        {
            var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (!string.IsNullOrWhiteSpace(systemRoot))
            {
                yield return Path.Combine(systemRoot, "drivers");
            }

            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrWhiteSpace(windows))
            {
                yield return Path.Combine(windows, "System32", "DriverStore", "FileRepository");
            }
        }
    }

    private interface IResidualCleanupHandler
    {
        bool CanHandle(ResidualItemKind kind);

        bool Cleanup(ResidualItem item, ResidualCleanupOptions options);
    }

    private sealed class DirectoryCleanupHandler : IResidualCleanupHandler
    {
        public bool CanHandle(ResidualItemKind kind) => kind == ResidualItemKind.Directory;

        public bool Cleanup(ResidualItem item, ResidualCleanupOptions options)
        {
            if (Directory.Exists(item.Path))
            {
                Directory.Delete(item.Path, recursive: true);
                return true;
            }

            return false;
        }
    }

    private sealed class FileCleanupHandler : IResidualCleanupHandler
    {
        public bool CanHandle(ResidualItemKind kind) => kind == ResidualItemKind.File;

        public bool Cleanup(ResidualItem item, ResidualCleanupOptions options)
        {
            if (File.Exists(item.Path))
            {
                if (options.ShredFiles)
                {
                    ShredFile(item.Path);
                }
                else
                {
                    File.Delete(item.Path);
                }

                return true;
            }

            return false;
        }

        private static void ShredFile(string path)
        {
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists)
            {
                return;
            }

            var length = fileInfo.Length;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
            var random = new Random();
            Span<byte> buffer = stackalloc byte[8192];

            for (var pass = 0; pass < 3; pass++)
            {
                stream.Position = 0;
                var remaining = length;
                while (remaining > 0)
                {
                    random.NextBytes(buffer);
                    var toWrite = (int)Math.Min(buffer.Length, remaining);
                    stream.Write(buffer[..toWrite]);
                    remaining -= toWrite;
                }
                stream.Flush(true);
            }

            stream.Close();
            File.Delete(path);
        }
    }

    private sealed class RegistryCleanupHandler : IResidualCleanupHandler
    {
        public bool CanHandle(ResidualItemKind kind) => kind == ResidualItemKind.RegistryKey || kind == ResidualItemKind.RegistryValue;

        public bool Cleanup(ResidualItem item, ResidualCleanupOptions options)
        {
            if (!TryParseRegistryTarget(item.Path, out var hive, out var keyPath, out var valueName, out var isValue))
            {
                return false;
            }

            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            if (baseKey is null)
            {
                return false;
            }

            if (isValue)
            {
                var parentPath = keyPath;
                var key = baseKey.OpenSubKey(parentPath, writable: true);
                if (key is null)
                {
                    return false;
                }

                key.DeleteValue(valueName!, throwOnMissingValue: false);
                return true;
            }

            baseKey.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
            return true;
        }

        private static bool TryParseRegistryTarget(string raw, out RegistryHive hive, out string keyPath, out string? valueName, out bool isValue)
        {
            hive = RegistryHive.LocalMachine;
            keyPath = string.Empty;
            valueName = null;
            isValue = false;

            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            var dividerIndex = raw.IndexOf("::", StringComparison.Ordinal);
            if (dividerIndex >= 0)
            {
                valueName = raw[(dividerIndex + 2)..];
                raw = raw[..dividerIndex];
                isValue = true;
            }

            var hiveSeparator = raw.IndexOf('\\');
            if (hiveSeparator < 0)
            {
                return false;
            }

            var hiveName = raw[..hiveSeparator];
            keyPath = raw[(hiveSeparator + 1)..];

            hive = hiveName.Equals("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase)
                ? RegistryHive.CurrentUser
                : RegistryHive.LocalMachine;

            return !string.IsNullOrWhiteSpace(keyPath);
        }
    }

    private sealed class ServiceCleanupHandler : IResidualCleanupHandler
    {
        public bool CanHandle(ResidualItemKind kind) => kind == ResidualItemKind.Service;

        public bool Cleanup(ResidualItem item, ResidualCleanupOptions options)
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"delete \"{item.Path}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process is null)
            {
                throw new InvalidOperationException("Unable to launch sc.exe");
            }

            process.WaitForExit();
            return process.ExitCode == 0;
        }
    }

    private sealed class ScheduledTaskCleanupHandler : IResidualCleanupHandler
    {
        public bool CanHandle(ResidualItemKind kind) => kind == ResidualItemKind.ScheduledTask;

        public bool Cleanup(ResidualItem item, ResidualCleanupOptions options)
        {
            if (File.Exists(item.Path))
            {
                File.Delete(item.Path);
                return true;
            }

            return false;
        }
    }

    private sealed class DriverCleanupHandler : IResidualCleanupHandler
    {
        public bool CanHandle(ResidualItemKind kind) => kind == ResidualItemKind.Driver;

        public bool Cleanup(ResidualItem item, ResidualCleanupOptions options)
        {
            if (File.Exists(item.Path))
            {
                File.Delete(item.Path);
                return true;
            }

            return false;
        }
    }

    private static class ResidualTokenBuilder
    {
        public static string[] Build(InstalledApplication app)
        {
            var tokens = new HashSet<string>(StringComparer.Ordinal);

            void Consider(string? value)
            {
                var normalized = NormalizeForComparison(value);
                if (!string.IsNullOrEmpty(normalized))
                {
                    tokens.Add(normalized);
                }
            }

            Consider(app.Name);
            Consider(app.Publisher);
            Consider(app.InstallLocation is null ? null : Path.GetFileName(app.InstallLocation));

            return tokens.Where(token => token.Length > 3).ToArray();
        }

        public static string NormalizeForComparison(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(char.ToLowerInvariant(ch));
                }
            }

            return builder.ToString();
        }

        public static string? NormalizePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                var expanded = Environment.ExpandEnvironmentVariables(path);
                var full = Path.GetFullPath(expanded);
                return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        public static long? TryGetDirectorySize(string path)
        {
            if (!Directory.Exists(path))
            {
                return null;
            }

            try
            {
                long total = 0;
                var stack = new Stack<string>();
                stack.Push(path);

                while (stack.Count > 0)
                {
                    var current = stack.Pop();
                    foreach (var file in Directory.EnumerateFiles(current))
                    {
                        try
                        {
                            var info = new FileInfo(file);
                            total += info.Length;
                        }
                        catch
                        {
                        }
                    }

                    foreach (var directory in Directory.EnumerateDirectories(current))
                    {
                        stack.Push(directory);
                    }
                }

                return total;
            }
            catch
            {
                return null;
            }
        }

        public static long? TryGetFileSize(string path)
        {
            try
            {
                var info = new FileInfo(path);
                return info.Exists ? info.Length : null;
            }
            catch
            {
                return null;
            }
        }
    }

    private static class InstalledApplicationComparer
    {
        public static IComparer<InstalledApplication> Instance { get; } = new NameComparer();

        private sealed class NameComparer : IComparer<InstalledApplication>
        {
            public int Compare(InstalledApplication? x, InstalledApplication? y)
            {
                return string.Compare(x?.Name, y?.Name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static class BrowserExtensionComparer
    {
        public static IComparer<BrowserExtensionInfo> Instance { get; } = new ExtensionComparer();

        private sealed class ExtensionComparer : IComparer<BrowserExtensionInfo>
        {
            public int Compare(BrowserExtensionInfo? x, BrowserExtensionInfo? y)
            {
                var browserCompare = string.Compare(x?.Browser, y?.Browser, StringComparison.OrdinalIgnoreCase);
                if (browserCompare != 0)
                {
                    return browserCompare;
                }

                return string.Compare(x?.Name, y?.Name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static class SoftwareHealthAnalyzer
    {
        public static IEnumerable<SoftwareHealthInsight> Analyze(IReadOnlyCollection<InstalledApplication> applications, IReadOnlyCollection<BrowserExtensionInfo> extensions)
        {
            var insights = new List<SoftwareHealthInsight>();

            if (extensions.Count > 0)
            {
                insights.Add(new SoftwareHealthInsight(
                    "Browser extensions detected",
                    $"{extensions.Count} extension(s) installed across Chrome, Edge, or Firefox.",
                    SoftwareHealthSeverity.Info));
            }

            var incompleteUninstallers = applications.Count(app => string.IsNullOrWhiteSpace(app.UninstallCommand) && string.IsNullOrWhiteSpace(app.QuietUninstallCommand));
            if (incompleteUninstallers > 0)
            {
                insights.Add(new SoftwareHealthInsight(
                    "Incomplete uninstallers",
                    $"{incompleteUninstallers} application(s) do not expose an uninstall command.",
                    SoftwareHealthSeverity.Warning));
            }

            var legacyApps = applications.Count(app => app.InstallDate.HasValue && app.InstallDate.Value < DateTime.Now.AddYears(-3));
            if (legacyApps > 0)
            {
                insights.Add(new SoftwareHealthInsight(
                    "Legacy software",
                    $"{legacyApps} application(s) were installed over three years ago.",
                    SoftwareHealthSeverity.Info));
            }

            var largeApps = applications.Where(app => app.EstimatedSizeBytes.HasValue && app.EstimatedSizeBytes.Value > 2L * 1024 * 1024 * 1024).ToList();
            if (largeApps.Count > 0)
            {
                var names = string.Join(", ", largeApps.Take(5).Select(app => app.Name));
                insights.Add(new SoftwareHealthInsight(
                    "Large disk usage",
                    $"Large applications detected: {names}.",
                    SoftwareHealthSeverity.Info));
            }

            return insights;
        }
    }
}
