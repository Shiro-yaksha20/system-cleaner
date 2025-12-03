using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LibreHardwareMonitor.Hardware;
using SystemCleaner.App.ViewModels;

namespace SystemCleaner.App.Services;

public sealed class HardwareMonitorService : IHardwareMonitorService
{
    private readonly Computer _computer;
    private readonly Timer _timer;
    private readonly object _sync = new();
    private bool _isRunning;
    private bool _disposed;
    private readonly TimeSpan _pollInterval;
    private readonly SynchronizationContext _syncContext;

    public HardwareMonitorService(TimeSpan? pollInterval = null)
    {
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(2);
        _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsControllerEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsNetworkEnabled = true,
            IsStorageEnabled = true
        };
        _computer.Open();
        _timer = new Timer(OnTimerTick, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public event EventHandler<HardwareSnapshotEventArgs>? SnapshotAvailable;

    public void Start()
    {
        ThrowIfDisposed();
        if (_isRunning)
        {
            return;
        }

        _isRunning = true;
        _timer.Change(TimeSpan.Zero, _pollInterval);
    }

    public void Stop()
    {
        if (!_isRunning)
        {
            return;
        }

        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _isRunning = false;
    }

    private void OnTimerTick(object? state)
    {
        if (_disposed)
        {
            return;
        }

        HardwareSnapshot snapshot;
        lock (_sync)
        {
            foreach (var hardware in _computer.Hardware)
            {
                UpdateRecursive(hardware);
            }

            snapshot = HardwareSnapshot.Create(_computer.Hardware);
        }

        _syncContext.Post(_ => SnapshotAvailable?.Invoke(this, new HardwareSnapshotEventArgs(snapshot)), null);
    }

    private static void UpdateRecursive(IHardware hardware)
    {
        hardware.Update();
        foreach (var sub in hardware.SubHardware)
        {
            UpdateRecursive(sub);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(HardwareMonitorService));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _timer.Dispose();
        _computer.Close();
        _disposed = true;
    }
}

public sealed class HardwareSnapshotEventArgs : EventArgs
{
    public HardwareSnapshotEventArgs(HardwareSnapshot snapshot)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }

    public HardwareSnapshot Snapshot { get; }
}

public sealed record HardwareSnapshot(
    DateTime Timestamp,
    HardwareSnapshot.ComponentSnapshot? Cpu,
    HardwareSnapshot.ComponentSnapshot? Gpu,
    HardwareSnapshot.MemorySnapshot? Memory,
    IReadOnlyList<HardwareSnapshot.StorageSnapshot> Storage)
{
    public sealed record ComponentSnapshot(
        string Name,
        double? UsagePercent,
        double? TemperatureCelsius,
        double? CoreClockMhz,
        double? MemoryClockMhz,
        double? VoltageMillivolts,
        double? PowerWatts)
    {
        public double? DisplayUsage => UsagePercent;

        public bool HasTelemetry => UsagePercent.HasValue || TemperatureCelsius.HasValue || CoreClockMhz.HasValue ||
                                     MemoryClockMhz.HasValue || VoltageMillivolts.HasValue || PowerWatts.HasValue;
    }

    public sealed record MemorySnapshot(
        string Name,
        double? UsagePercent,
        double? UsedGigabytes,
        double? AvailableGigabytes,
        double? TotalGigabytes)
    {
        public bool HasTelemetry => UsagePercent.HasValue || UsedGigabytes.HasValue || AvailableGigabytes.HasValue || TotalGigabytes.HasValue;
    }

    public sealed record StorageSnapshot(
        string Name,
        double? TemperatureCelsius,
        double? RemainingLifePercent,
        double? UsagePercent,
        string? HealthStatus);

    public static HardwareSnapshot Create(IEnumerable<IHardware> hardwareItems)
    {
        if (hardwareItems is null)
        {
            throw new ArgumentNullException(nameof(hardwareItems));
        }

        ComponentSnapshot? cpu = null;
        ComponentSnapshot? gpu = null;
        MemorySnapshot? memory = null;
        var storage = new List<StorageSnapshot>();

        foreach (var hardware in hardwareItems)
        {
            switch (hardware.HardwareType)
            {
                case HardwareType.Cpu:
                    cpu = BuildComponentSnapshot(hardware, includePower: true);
                    break;
                case HardwareType.GpuAmd:
                case HardwareType.GpuNvidia:
                case HardwareType.GpuIntel:
                    gpu = BuildComponentSnapshot(hardware, includePower: true);
                    break;
                case HardwareType.Memory:
                    memory = BuildMemorySnapshot(hardware);
                    break;
                case HardwareType.Storage:
                    storage.Add(BuildStorageSnapshot(hardware));
                    break;
            }
        }

        return new HardwareSnapshot(DateTime.UtcNow, cpu, gpu, memory, storage);
    }

    private static ComponentSnapshot BuildComponentSnapshot(IHardware hardware, bool includePower)
    {
        static double? FindValue(IEnumerable<ISensor> sensors, SensorType type, Func<ISensor, bool>? predicate = null)
        {
            var query = sensors.Where(sensor => sensor.SensorType == type);
            if (predicate != null)
            {
                query = query.Where(predicate);
            }

            return query.Select(sensor => (double?)sensor.Value).FirstOrDefault(value => value.HasValue);
        }

        var sensors = hardware.Sensors;
        var name = hardware.Name;
        var usage = CleanValue(FindValue(sensors, SensorType.Load, sensor => sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))
                               ?? FindValue(sensors, SensorType.Load));
        var temp = CleanValue(FindValue(sensors, SensorType.Temperature), allowZero: false);
        var coreClock = CleanValue(FindValue(sensors, SensorType.Clock, sensor => sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                                   ?? FindValue(sensors, SensorType.Clock), allowZero: false);
        var memClock = CleanValue(FindValue(sensors, SensorType.Clock, sensor => sensor.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase)), allowZero: false);
        var voltage = CleanValue(FindValue(sensors, SensorType.Voltage, sensor => sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                                 ?? FindValue(sensors, SensorType.Voltage), allowZero: false);
        var power = includePower
            ? CleanValue(FindValue(sensors, SensorType.Power, sensor => sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))
                         ?? FindValue(sensors, SensorType.Power), allowZero: false)
            : null;

        return new ComponentSnapshot(name, usage, temp, coreClock, memClock, voltage, power);

        static double? CleanValue(double? value, bool allowZero = true)
        {
            if (!value.HasValue)
            {
                return null;
            }

            var sample = value.Value;
            if (double.IsNaN(sample) || double.IsInfinity(sample))
            {
                return null;
            }

            if (!allowZero && sample <= 0)
            {
                return null;
            }

            return sample;
        }
    }

    private static MemorySnapshot BuildMemorySnapshot(IHardware hardware)
    {
        static double? FindValue(IEnumerable<ISensor> sensors, SensorType type, Func<ISensor, bool>? predicate = null)
        {
            var query = sensors.Where(sensor => sensor.SensorType == type);
            if (predicate is not null)
            {
                query = query.Where(predicate);
            }

            return query.Select(sensor => (double?)sensor.Value).FirstOrDefault(value => value.HasValue);
        }

        var sensors = hardware.Sensors;
        var usage = CleanValue(FindValue(sensors, SensorType.Load, sensor => sensor.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase))
                               ?? FindValue(sensors, SensorType.Load));
        var used = CleanValue(FindValue(sensors, SensorType.Data, sensor => sensor.Name.Contains("Used", StringComparison.OrdinalIgnoreCase)));
        var available = CleanValue(FindValue(sensors, SensorType.Data, sensor => sensor.Name.Contains("Available", StringComparison.OrdinalIgnoreCase)));
        var total = CleanValue(FindValue(sensors, SensorType.Data, sensor => sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase)));

        if (!total.HasValue && used.HasValue && available.HasValue)
        {
            total = used + available;
        }

        return new MemorySnapshot(hardware.Name, usage, used, available, total);

        static double? CleanValue(double? value)
        {
            if (!value.HasValue)
            {
                return null;
            }

            var sample = value.Value;
            if (double.IsNaN(sample) || double.IsInfinity(sample))
            {
                return null;
            }

            return sample;
        }
    }

    private static StorageSnapshot BuildStorageSnapshot(IHardware hardware)
    {
        static double? FirstValue(IEnumerable<ISensor> sensors, SensorType type) => sensors.Where(sensor => sensor.SensorType == type)
                                                                                          .Select(sensor => (double?)sensor.Value)
                                                                                          .FirstOrDefault(value => value.HasValue);

        var temp = FirstValue(hardware.Sensors, SensorType.Temperature);
        var life = FirstValue(hardware.Sensors, SensorType.Level);
        var usage = FirstValue(hardware.Sensors, SensorType.Load);
        return new StorageSnapshot(hardware.Name, temp, life, usage, null);
    }
}
