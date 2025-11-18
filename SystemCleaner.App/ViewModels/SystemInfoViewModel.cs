using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Management;
using SystemCleaner.App.Services;
using SystemCleaner.App.Utilities;

namespace SystemCleaner.App.ViewModels;

public sealed class SystemInfoViewModel : ObservableObject
{
    private string _cpuTemperature = "—";
    private string _cpuUsage = "—";
    private string _cpuClock = "—";
    private string _gpuTemperature = "—";
    private string _gpuUsage = "—";
    private string _gpuClock = "—";

    public SystemInfoViewModel()
    {
        StorageDevices = new ObservableCollection<StorageDeviceViewModel>();
        LogicalDrives = new ObservableCollection<LogicalDriveViewModel>();
        MachineName = Environment.MachineName;
        OperatingSystem = Environment.OSVersion.VersionString;
        (Manufacturer, Model) = QueryManufacturerAndModel();
        BiosVersion = QueryBiosVersion();
        InstalledMemory = QueryInstalledMemory();
        UpdateLogicalDrives();
    }

    public string MachineName { get; }

    public string OperatingSystem { get; }

    public string Manufacturer { get; }

    public string Model { get; }

    public string BiosVersion { get; }

    public string InstalledMemory { get; }

    public ObservableCollection<StorageDeviceViewModel> StorageDevices { get; }

    public ObservableCollection<LogicalDriveViewModel> LogicalDrives { get; }

    public string CpuTemperature
    {
        get => _cpuTemperature;
        private set => SetProperty(ref _cpuTemperature, value);
    }

    public string CpuUsage
    {
        get => _cpuUsage;
        private set => SetProperty(ref _cpuUsage, value);
    }

    public string CpuClock
    {
        get => _cpuClock;
        private set => SetProperty(ref _cpuClock, value);
    }

    public string GpuTemperature
    {
        get => _gpuTemperature;
        private set => SetProperty(ref _gpuTemperature, value);
    }

    public string GpuUsage
    {
        get => _gpuUsage;
        private set => SetProperty(ref _gpuUsage, value);
    }

    public string GpuClock
    {
        get => _gpuClock;
        private set => SetProperty(ref _gpuClock, value);
    }

    public void Update(HardwareSnapshot snapshot)
    {
        if (snapshot is null)
        {
            return;
        }

        if (snapshot.Cpu is not null)
        {
            CpuTemperature = FormatTemperature(snapshot.Cpu.TemperatureCelsius);
            CpuUsage = FormatPercentage(snapshot.Cpu.UsagePercent);
            CpuClock = FormatClock(snapshot.Cpu.CoreClockMhz);
        }

        if (snapshot.Gpu is not null)
        {
            GpuTemperature = FormatTemperature(snapshot.Gpu.TemperatureCelsius);
            GpuUsage = FormatPercentage(snapshot.Gpu.UsagePercent);
            GpuClock = FormatClock(snapshot.Gpu.CoreClockMhz);
        }

        UpdateStorage(snapshot.Storage);
        UpdateLogicalDrives();
    }

    private void UpdateStorage(IReadOnlyList<HardwareSnapshot.StorageSnapshot> storage)
    {
        var existingByName = StorageDevices.ToDictionary(device => device.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var snapshot in storage)
        {
            if (existingByName.TryGetValue(snapshot.Name, out var device))
            {
                device.Update(snapshot);
            }
            else
            {
                StorageDevices.Add(StorageDeviceViewModel.FromSnapshot(snapshot));
            }
        }

        // remove devices no longer reported
        for (var index = StorageDevices.Count - 1; index >= 0; index--)
        {
            if (!storage.Any(snapshot => string.Equals(snapshot.Name, StorageDevices[index].Name, StringComparison.OrdinalIgnoreCase)))
            {
                StorageDevices.RemoveAt(index);
            }
        }
    }

    private void UpdateLogicalDrives()
    {
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives()
                               .Where(drive => drive.IsReady && (drive.DriveType == DriveType.Fixed || drive.DriveType == DriveType.Removable))
                               .ToArray();
        }
        catch
        {
            drives = Array.Empty<DriveInfo>();
        }

        var lookup = LogicalDrives.ToDictionary(drive => drive.Name, StringComparer.OrdinalIgnoreCase);
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var drive in drives)
        {
            var name = drive.Name.TrimEnd('\\');
            if (!lookup.TryGetValue(name, out var viewModel))
            {
                viewModel = new LogicalDriveViewModel(name);
                LogicalDrives.Add(viewModel);
            }

            viewModel.Update(drive);
            present.Add(name);
        }

        for (var index = LogicalDrives.Count - 1; index >= 0; index--)
        {
            if (!present.Contains(LogicalDrives[index].Name))
            {
                LogicalDrives.RemoveAt(index);
            }
        }
    }

    private static string FormatTemperature(double? value) => value.HasValue ? $"{value.Value:F1} °C" : "—";

    private static string FormatPercentage(double? value) => value.HasValue ? $"{value.Value:F0}%" : "—";

    private static string FormatClock(double? value) => value.HasValue ? $"{value.Value:F0} MHz" : "—";

    private static (string Manufacturer, string Model) QueryManufacturerAndModel()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem");
            foreach (var managementObject in searcher.Get())
            {
                var manufacturer = managementObject["Manufacturer"]?.ToString() ?? "Unknown";
                var model = managementObject["Model"]?.ToString() ?? "Unknown";
                return (manufacturer, model);
            }
        }
        catch
        {
            // ignored
        }

        return ("Unknown", "Unknown");
    }

    public sealed class LogicalDriveViewModel : ObservableObject
    {
        private string _label = string.Empty;
        private string _total = "—";
        private string _free = "—";
        private double _usagePercent;

        public LogicalDriveViewModel(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public string Label
        {
            get => _label;
            private set => SetProperty(ref _label, value);
        }

        public string Total
        {
            get => _total;
            private set => SetProperty(ref _total, value);
        }

        public string Free
        {
            get => _free;
            private set => SetProperty(ref _free, value);
        }

        public double UsagePercent
        {
            get => _usagePercent;
            private set => SetProperty(ref _usagePercent, value);
        }

        internal void Update(DriveInfo drive)
        {
            var total = drive.TotalSize;
            var free = drive.TotalFreeSpace;
            var used = Math.Max(0, total - free);
            Label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.Name.TrimEnd('\\') : drive.VolumeLabel;
            Total = SizeFormatter.FormatSize(total);
            Free = SizeFormatter.FormatSize(free);
            UsagePercent = total > 0 ? (used / (double)total) * 100 : 0;
        }
    }

    private static string QueryBiosVersion()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion FROM Win32_BIOS");
            foreach (var managementObject in searcher.Get())
            {
                return managementObject["SMBIOSBIOSVersion"]?.ToString() ?? "Unknown";
            }
        }
        catch
        {
            // ignored
        }

        return "Unknown";
    }

    private static string QueryInstalledMemory()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (var managementObject in searcher.Get())
            {
                if (managementObject["TotalPhysicalMemory"] is ulong bytes)
                {
                    var gigabytes = bytes / (1024d * 1024d * 1024d);
                    return $"{gigabytes:F1} GB";
                }
            }
        }
        catch
        {
            // ignored
        }

        return "Unknown";
    }

    public sealed class StorageDeviceViewModel : ObservableObject
    {
        private string _temperature = "—";
        private string _lifeRemaining = "—";
        private string _usage = "—";
        private string _health = "—";

        public StorageDeviceViewModel(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public string Temperature
        {
            get => _temperature;
            private set => SetProperty(ref _temperature, value);
        }

        public string LifeRemaining
        {
            get => _lifeRemaining;
            private set => SetProperty(ref _lifeRemaining, value);
        }

        public string Usage
        {
            get => _usage;
            private set => SetProperty(ref _usage, value);
        }

        public string Health
        {
            get => _health;
            private set => SetProperty(ref _health, value);
        }

        internal static StorageDeviceViewModel FromSnapshot(HardwareSnapshot.StorageSnapshot snapshot)
        {
            var viewModel = new StorageDeviceViewModel(snapshot.Name);
            viewModel.Update(snapshot);
            return viewModel;
        }

        internal void Update(HardwareSnapshot.StorageSnapshot snapshot)
        {
            Temperature = FormatTemperature(snapshot.TemperatureCelsius);
            LifeRemaining = snapshot.RemainingLifePercent.HasValue ? $"{snapshot.RemainingLifePercent.Value:F0}%" : "—";
            Usage = snapshot.UsagePercent.HasValue ? $"{snapshot.UsagePercent.Value:F0}%" : "—";
            Health = snapshot.HealthStatus ?? "—";
        }
    }
}
