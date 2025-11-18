using System;
using System.IO;
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

    public AppSettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(appData, "SystemCleaner");
        Directory.CreateDirectory(folder);
        _settingsPath = Path.Combine(folder, "settings.json");
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
                return _cached.Clone();
            }

            await using var stream = new FileStream(_settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            _cached = await JsonSerializer.DeserializeAsync<AppSettings>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false) ?? new AppSettings();
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
            await using var stream = new FileStream(_settingsPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
            await JsonSerializer.SerializeAsync(stream, _cached, SerializerOptions, cancellationToken).ConfigureAwait(false);
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
}
