using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SystemCleaner.App.Services;

namespace SystemCleaner.App.Settings;

public interface IAppSettingsService
{
    Task<AppSettings> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

public sealed class AppSettingsService : IAppSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;
    private readonly SemaphoreSlim _syncRoot = new(1, 1);
    private AppSettings? _cached;
    private readonly string _apiKeyPath;

    public AppSettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(appData, "SystemCleaner");
        Directory.CreateDirectory(folder);
        _settingsPath = Path.Combine(folder, "settings.json");
        _apiKeyPath = Path.Combine(folder, "virustotal.key");
    }

    public async Task<AppSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        await _syncRoot.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cached is not null)
            {
                return _cached.Clone();
            }

            if (!File.Exists(_settingsPath))
            {
                _cached = new AppSettings();
                await PopulateVirusTotalKeyAsync(_cached, cancellationToken).ConfigureAwait(false);
                return _cached.Clone();
            }

            await using var stream = new FileStream(_settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            _cached = await JsonSerializer.DeserializeAsync<AppSettings>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false) ?? new AppSettings();
            await PopulateVirusTotalKeyAsync(_cached, cancellationToken).ConfigureAwait(false);
            return _cached.Clone();
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log(ex, nameof(AppSettingsService));
            _cached = new AppSettings();
            return _cached.Clone();
        }
        finally
        {
            _syncRoot.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        await _syncRoot.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _cached = settings.Clone();
            await PersistVirusTotalKeyAsync(_cached.VirusTotalApiKey, cancellationToken).ConfigureAwait(false);
            _cached.HasVirusTotalApiKey = !string.IsNullOrWhiteSpace(_cached.VirusTotalApiKey);

            var persisted = _cached.Clone();
            persisted.VirusTotalApiKey = null;

            await using var stream = new FileStream(_settingsPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
            await JsonSerializer.SerializeAsync(stream, persisted, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log(ex, nameof(AppSettingsService));
        }
        finally
        {
            _syncRoot.Release();
        }
    }

    private async Task PopulateVirusTotalKeyAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(settings.VirusTotalApiKey))
            {
                await PersistVirusTotalKeyAsync(settings.VirusTotalApiKey, cancellationToken).ConfigureAwait(false);
                settings.HasVirusTotalApiKey = true;
                return;
            }

            var key = await RetrieveVirusTotalKeyAsync(cancellationToken).ConfigureAwait(false);
            settings.VirusTotalApiKey = key;
            settings.HasVirusTotalApiKey = !string.IsNullOrWhiteSpace(key);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log(ex, nameof(AppSettingsService));
            settings.VirusTotalApiKey = null;
            settings.HasVirusTotalApiKey = false;
        }
    }

    private async Task PersistVirusTotalKeyAsync(string? apiKey, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                if (File.Exists(_apiKeyPath))
                {
                    File.Delete(_apiKeyPath);
                }
                return;
            }

            var plainBytes = Encoding.UTF8.GetBytes(apiKey);
            var protectedBytes = ProtectedData.Protect(plainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            await File.WriteAllBytesAsync(_apiKeyPath, protectedBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log(ex, nameof(AppSettingsService));
        }
    }

    private async Task<string?> RetrieveVirusTotalKeyAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_apiKeyPath))
        {
            return null;
        }

        try
        {
            var encrypted = await File.ReadAllBytesAsync(_apiKeyPath, cancellationToken).ConfigureAwait(false);
            if (encrypted.Length == 0)
            {
                return null;
            }

            var plainBytes = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes).Trim();
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log(ex, nameof(AppSettingsService));
            return null;
        }
    }
}
