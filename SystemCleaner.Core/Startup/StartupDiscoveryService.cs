using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using SystemCleaner.Core.Models;

namespace SystemCleaner.Core.Startup;

[SupportedOSPlatform("windows")]
public sealed class StartupDiscoveryService : IStartupDiscoveryService
{
    private sealed record RegistryLocation(
        string DisplayName,
        RegistryHive Hive,
        RegistryView View,
        string SubKey,
        RegistryView ApprovalView,
        string ApprovalSubKey,
        string Scope);

    private sealed record ApprovalLocation(
        string Scope,
        RegistryHive Hive,
        RegistryView View,
        string SubKey,
        string DisplayName);

    private static readonly RegistryLocation[] RegistryLocations =
    {
        new("Registry (HKCU Run, 64-bit)", RegistryHive.CurrentUser, RegistryView.Registry64, "Software\\Microsoft\\Windows\\CurrentVersion\\Run", RegistryView.Registry64, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run", "Current User"),
        new("Registry (HKCU RunOnce, 64-bit)", RegistryHive.CurrentUser, RegistryView.Registry64, "Software\\Microsoft\\Windows\\CurrentVersion\\RunOnce", RegistryView.Registry64, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\RunOnce", "Current User"),
        new("Registry (HKCU Run, 32-bit)", RegistryHive.CurrentUser, RegistryView.Registry32, "Software\\Microsoft\\Windows\\CurrentVersion\\Run", RegistryView.Registry64, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run32", "Current User"),
        new("Registry (HKCU RunOnce, 32-bit)", RegistryHive.CurrentUser, RegistryView.Registry32, "Software\\Microsoft\\Windows\\CurrentVersion\\RunOnce", RegistryView.Registry64, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\RunOnce32", "Current User"),
        new("Registry (HKLM Run, 64-bit)", RegistryHive.LocalMachine, RegistryView.Registry64, "Software\\Microsoft\\Windows\\CurrentVersion\\Run", RegistryView.Registry64, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run", "All Users"),
        new("Registry (HKLM RunOnce, 64-bit)", RegistryHive.LocalMachine, RegistryView.Registry64, "Software\\Microsoft\\Windows\\CurrentVersion\\RunOnce", RegistryView.Registry64, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\RunOnce", "All Users"),
        new("Registry (HKLM Run, 32-bit)", RegistryHive.LocalMachine, RegistryView.Registry32, "Software\\Microsoft\\Windows\\CurrentVersion\\Run", RegistryView.Registry64, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run32", "All Users"),
        new("Registry (HKLM RunOnce, 32-bit)", RegistryHive.LocalMachine, RegistryView.Registry32, "Software\\Microsoft\\Windows\\CurrentVersion\\RunOnce", RegistryView.Registry64, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\RunOnce32", "All Users")
    };

    private static readonly ApprovalLocation[] ApprovalLocations =
    {
        new("Current User", RegistryHive.CurrentUser, RegistryView.Registry64, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run", "StartupApproved (HKCU Run, 64-bit)"),
        new("Current User", RegistryHive.CurrentUser, RegistryView.Registry64, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run32", "StartupApproved (HKCU Run32, 64-bit)"),
        new("Current User", RegistryHive.CurrentUser, RegistryView.Registry64, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\RunOnce", "StartupApproved (HKCU RunOnce, 64-bit)"),
        new("Current User", RegistryHive.CurrentUser, RegistryView.Registry64, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\RunOnce32", "StartupApproved (HKCU RunOnce32, 64-bit)"),
        new("Current User", RegistryHive.CurrentUser, RegistryView.Registry32, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run", "StartupApproved (HKCU Run, 32-bit)"),
        new("Current User", RegistryHive.CurrentUser, RegistryView.Registry32, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\RunOnce", "StartupApproved (HKCU RunOnce, 32-bit)"),
        new("All Users", RegistryHive.LocalMachine, RegistryView.Registry64, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run", "StartupApproved (HKLM Run, 64-bit)"),
        new("All Users", RegistryHive.LocalMachine, RegistryView.Registry64, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run32", "StartupApproved (HKLM Run32, 64-bit)"),
        new("All Users", RegistryHive.LocalMachine, RegistryView.Registry64, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\RunOnce", "StartupApproved (HKLM RunOnce, 64-bit)"),
        new("All Users", RegistryHive.LocalMachine, RegistryView.Registry64, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\RunOnce32", "StartupApproved (HKLM RunOnce32, 64-bit)"),
        new("All Users", RegistryHive.LocalMachine, RegistryView.Registry32, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run", "StartupApproved (HKLM Run, 32-bit)"),
        new("All Users", RegistryHive.LocalMachine, RegistryView.Registry32, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\RunOnce", "StartupApproved (HKLM RunOnce, 32-bit)"),
        new("Current User", RegistryHive.CurrentUser, RegistryView.Registry64, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\StartupFolder", "StartupApproved (HKCU Startup Folder, 64-bit)"),
        new("Current User", RegistryHive.CurrentUser, RegistryView.Registry32, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\StartupFolder", "StartupApproved (HKCU Startup Folder, 32-bit)"),
        new("All Users", RegistryHive.LocalMachine, RegistryView.Registry64, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\StartupFolder", "StartupApproved (HKLM Startup Folder, 64-bit)"),
        new("All Users", RegistryHive.LocalMachine, RegistryView.Registry32, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\StartupFolder", "StartupApproved (HKLM Startup Folder, 32-bit)")
    };

    private const byte EnabledStateValue = 2;
    private const byte DisabledStateValue = 3;
    private const int ApprovalStatePayloadLength = 12;

    public Task<StartupDiscoveryResult> GetStartupEntriesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var entries = new List<StartupEntry>();
            var issues = new List<string>();
            var approvalStates = LoadApprovalStates(issues, cancellationToken);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var location in RegistryLocations)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(location.Hive, location.View);
                    using var key = baseKey.OpenSubKey(location.SubKey);
                    if (key is null)
                    {
                        continue;
                    }

                    foreach (var valueName in SafeGetValueNames(key))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var command = GetRegistryValueAsString(key, valueName);
                        var name = string.IsNullOrWhiteSpace(valueName) ? "(Default)" : valueName;
                        var uniqueKey = $"{location.Scope}|{name}|{command}";
                        if (!seen.Add(uniqueKey))
                        {
                            continue;
                        }

                        var stateKey = $"{location.Scope}|{name}";
                        var isEnabled = approvalStates.TryGetValue(stateKey, out var enabled) ? enabled : true;
                        var rawValueName = valueName ?? string.Empty;
                        entries.Add(new StartupEntry(
                            name,
                            location.Scope,
                            command,
                            isEnabled,
                            StartupEntryKind.Registry,
                            location.Hive,
                            location.View,
                            location.ApprovalView,
                            location.SubKey,
                            rawValueName,
                            location.ApprovalSubKey));
                    }
                }
                catch (Exception ex)
                {
                    issues.Add($"{location.DisplayName}: {ex.Message}");
                }
            }

            ReadStartupFolderEntries(
                Environment.SpecialFolder.Startup,
                "Current User Startup Folder",
                "Current User",
                RegistryHive.CurrentUser,
                RegistryView.Registry64,
                "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\StartupFolder",
                approvalStates,
                entries,
                issues,
                seen,
                cancellationToken);
            ReadStartupFolderEntries(
                Environment.SpecialFolder.CommonStartup,
                "All Users Startup Folder",
                "All Users",
                RegistryHive.LocalMachine,
                RegistryView.Registry64,
                "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\StartupFolder",
                approvalStates,
                entries,
                issues,
                seen,
                cancellationToken);

            var orderedEntries = entries
                .OrderBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static entry => entry.Location, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new StartupDiscoveryResult(orderedEntries, issues.ToArray());
        }, cancellationToken);
    }

    private static Dictionary<string, bool> LoadApprovalStates(ICollection<string> issues, CancellationToken cancellationToken)
    {
        var states = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var location in ApprovalLocations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(location.Hive, location.View);
                using var key = baseKey.OpenSubKey(location.SubKey);
                if (key is null)
                {
                    continue;
                }

                foreach (var valueName in SafeGetValueNames(key))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var state = GetApprovalState(key, valueName);
                    if (state is null)
                    {
                        continue;
                    }

                    var name = string.IsNullOrWhiteSpace(valueName) ? "(Default)" : valueName;
                    states[$"{location.Scope}|{name}"] = state.Value;
                }
            }
            catch (Exception ex)
            {
                issues.Add($"{location.DisplayName}: {ex.Message}");
            }
        }

        return states;
    }

    public Task SetStartupEntryEnabledAsync(StartupEntry entry, bool isEnabled, CancellationToken cancellationToken = default)
    {
        if (entry is null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var approvalView = entry.RegistryApprovalView ?? entry.RegistryView;

            if (entry.RegistryHive is null || approvalView is null || string.IsNullOrWhiteSpace(entry.ApprovalSubKey) || string.IsNullOrWhiteSpace(entry.RegistryValueName))
            {
                throw new StartupDiscoveryException("The selected startup entry does not support toggling.", new InvalidOperationException("Missing toggle metadata."));
            }

            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(entry.RegistryHive.Value, approvalView.Value);
                using var approvalKey = baseKey.CreateSubKey(entry.ApprovalSubKey, writable: true);
                if (approvalKey is null)
                {
                    throw new InvalidOperationException($"Unable to open approval registry key '{entry.ApprovalSubKey}'.");
                }

                cancellationToken.ThrowIfCancellationRequested();

                var valueName = entry.RegistryValueName;
                var existing = approvalKey.GetValue(valueName) as byte[];
                var desiredState = isEnabled ? EnabledStateValue : DisabledStateValue;

                byte[] payload;
                if (existing is { Length: > 0 })
                {
                    if (existing[0] == desiredState)
                    {
                        return;
                    }

                    payload = (byte[])existing.Clone();
                    payload[0] = desiredState;
                }
                else
                {
                    payload = CreateApprovalStateData(desiredState);
                }

                approvalKey.SetValue(valueName, payload, RegistryValueKind.Binary);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                string message;
                if (ex is UnauthorizedAccessException or System.Security.SecurityException)
                {
                    var scope = string.Equals(entry.Location, "All Users", StringComparison.OrdinalIgnoreCase)
                        ? "all users"
                        : "current user";
                    message = $"Administrator permissions are required to {(isEnabled ? "enable" : "disable")} startup items for {scope}.";
                }
                else
                {
                    message = $"Failed to {(isEnabled ? "enable" : "disable")} '{entry.Name}'.";
                }

                throw new StartupDiscoveryException(message, ex);
            }
        }, cancellationToken);
    }

    public Task<bool?> GetStartupEntryApprovalStateAsync(StartupEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry is null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        var approvalView = entry.RegistryApprovalView ?? entry.RegistryView;
        if (entry.RegistryHive is null || approvalView is null || string.IsNullOrWhiteSpace(entry.ApprovalSubKey) || string.IsNullOrWhiteSpace(entry.RegistryValueName))
        {
            return Task.FromResult<bool?>(null);
        }

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var baseKey = RegistryKey.OpenBaseKey(entry.RegistryHive.Value, approvalView.Value);
            using var approvalKey = baseKey.OpenSubKey(entry.ApprovalSubKey, writable: false);
            if (approvalKey is null)
            {
                return (bool?)null;
            }

            return GetApprovalState(approvalKey, entry.RegistryValueName);
        }, cancellationToken);
    }

    private static byte[] CreateApprovalStateData(byte stateValue)
    {
        var data = new byte[ApprovalStatePayloadLength];
        data[0] = stateValue;
        return data;
    }

    private static string[] SafeGetValueNames(RegistryKey key)
    {
        try
        {
            return key.GetValueNames();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string GetRegistryValueAsString(RegistryKey key, string valueName)
    {
        try
        {
            var value = key.GetValue(valueName);
            if (value is null)
            {
                return string.Empty;
            }

            return value switch
            {
                string str => str,
                string[] multi => string.Join(" ", multi.Where(static segment => !string.IsNullOrWhiteSpace(segment))),
                byte[] bytes => System.Text.Encoding.Default.GetString(bytes),
                _ => value.ToString() ?? string.Empty
            };
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool? GetApprovalState(RegistryKey key, string valueName)
    {
        try
        {
            if (key.GetValue(valueName) is not byte[] data || data.Length == 0)
            {
                return null;
            }

            return data[0] switch
            {
                2 => true,
                3 => false,
                var other => other != 0
            };
        }
        catch
        {
            return null;
        }
    }

    private static void ReadStartupFolderEntries(
        Environment.SpecialFolder folder,
        string displayName,
        string scope,
        RegistryHive approvalHive,
        RegistryView approvalView,
        string approvalSubKey,
        IReadOnlyDictionary<string, bool> approvalStates,
        ICollection<StartupEntry> entries,
        ICollection<string> issues,
        ISet<string> seen,
        CancellationToken cancellationToken)
    {
        string path;
        try
        {
            path = Environment.GetFolderPath(folder);
        }
        catch (Exception ex)
        {
            issues.Add($"{displayName}: {ex.Message}");
            return;
        }

        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(file);
                var uniqueKey = $"{scope}|{name}|{file}";
                if (!seen.Add(uniqueKey))
                {
                    continue;
                }

                var stateKey = $"{scope}|{name}";
                var isEnabled = approvalStates.TryGetValue(stateKey, out var enabled) ? enabled : true;

                entries.Add(new StartupEntry(
                    name,
                    scope,
                    file,
                    isEnabled,
                    StartupEntryKind.StartupFolder,
                    approvalHive,
                    registryView: null,
                    registryApprovalView: approvalView,
                    registrySubKey: null,
                    registryValueName: name,
                    approvalSubKey: approvalSubKey));
            }
        }
        catch (Exception ex)
        {
            issues.Add($"{displayName}: {ex.Message}");
        }
    }
}
