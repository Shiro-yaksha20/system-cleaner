using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using Microsoft.Win32;
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
    private string _uptimeDisplay = "—";
    private string _lastBootDisplay = "—";

    public SystemInfoViewModel()
    {
        StorageDevices = new ObservableCollection<StorageDeviceViewModel>();
        LogicalDrives = new ObservableCollection<LogicalDriveViewModel>();
        GraphicsAdapters = new ObservableCollection<GraphicsAdapterViewModel>(QueryGraphicsAdapters());
        MachineName = Environment.MachineName;
        WindowsDetails = QueryWindowsDetails();
        OperatingSystem = string.IsNullOrWhiteSpace(WindowsDetails.DisplayName)
            ? Environment.OSVersion.VersionString
            : WindowsDetails.DisplayName;
        (Manufacturer, Model) = QueryManufacturerAndModel();
        BiosVersion = QueryBiosVersion();
        InstalledMemory = QueryInstalledMemory();
        CpuDetails = QueryProcessorDetails();
        UpdateLogicalDrives();
        RefreshUptime();
    }

    public string MachineName { get; }

    public string OperatingSystem { get; }

    public string Manufacturer { get; }

    public string Model { get; }

    public string BiosVersion { get; }

    public string InstalledMemory { get; }

    public ObservableCollection<StorageDeviceViewModel> StorageDevices { get; }

    public ObservableCollection<LogicalDriveViewModel> LogicalDrives { get; }

    public ObservableCollection<GraphicsAdapterViewModel> GraphicsAdapters { get; }

    public ProcessorDetailsViewModel CpuDetails { get; }

    public WindowsInstallationDetailsViewModel WindowsDetails { get; }

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

    public string UptimeDisplay
    {
        get => _uptimeDisplay;
        private set => SetProperty(ref _uptimeDisplay, value);
    }

    public string LastBootDisplay
    {
        get => _lastBootDisplay;
        private set => SetProperty(ref _lastBootDisplay, value);
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
        RefreshUptime();
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

    private void RefreshUptime()
    {
        try
        {
            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            UptimeDisplay = FormatDuration(uptime);
            var lastBoot = DateTime.Now - uptime;
            LastBootDisplay = lastBoot.ToString("MMM dd, yyyy h:mm tt", CultureInfo.CurrentCulture);
        }
        catch
        {
            UptimeDisplay = "—";
            LastBootDisplay = "—";
        }
    }

    private static string FormatDuration(TimeSpan uptime)
    {
        if (uptime.TotalMinutes < 1)
        {
            return "< 1m";
        }

        var segments = new List<string>(3);
        if (uptime.Days > 0)
        {
            segments.Add($"{uptime.Days}d");
        }

        if (uptime.Hours > 0)
        {
            segments.Add($"{uptime.Hours}h");
        }

        segments.Add($"{uptime.Minutes}m");
        return string.Join(" ", segments);
    }

    private static ProcessorDetailsViewModel QueryProcessorDetails()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, Architecture, SocketDesignation, L2CacheSize, L3CacheSize FROM Win32_Processor");
            foreach (ManagementObject cpu in searcher.Get())
            {
                return new ProcessorDetailsViewModel
                {
                    Name = cpu["Name"]?.ToString() ?? "Unknown",
                    Manufacturer = cpu["Manufacturer"]?.ToString() ?? "Unknown",
                    Cores = cpu["NumberOfCores"] is uint cores
                        ? cores.ToString(CultureInfo.InvariantCulture)
                        : "—",
                    Threads = cpu["NumberOfLogicalProcessors"] is uint threads
                        ? threads.ToString(CultureInfo.InvariantCulture)
                        : "—",
                    BaseClock = cpu["MaxClockSpeed"] is uint clock
                        ? $"{clock} MHz"
                        : "—",
                    Architecture = DescribeArchitecture(cpu["Architecture"]),
                    Socket = cpu["SocketDesignation"]?.ToString() ?? "—",
                    L2Cache = FormatCacheSize(cpu["L2CacheSize"]),
                    L3Cache = FormatCacheSize(cpu["L3CacheSize"])
                };
            }
        }
        catch
        {
            // ignored
        }

        return ProcessorDetailsViewModel.CreateDefault();
    }

    private static IEnumerable<GraphicsAdapterViewModel> QueryGraphicsAdapters()
    {
        var adapters = new List<GraphicsAdapterViewModel>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, AdapterRAM, DriverVersion, DriverDate, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate, VideoModeDescription FROM Win32_VideoController");
            foreach (ManagementObject adapter in searcher.Get())
            {
                adapters.Add(new GraphicsAdapterViewModel
                {
                    Name = adapter["Name"]?.ToString() ?? "Unknown",
                    Memory = FormatAdapterMemory(adapter["AdapterRAM"]),
                    DriverVersion = adapter["DriverVersion"]?.ToString() ?? "—",
                    DriverDate = FormatDriverDate(adapter["DriverDate"]?.ToString()),
                    Resolution = FormatResolution(adapter["CurrentHorizontalResolution"], adapter["CurrentVerticalResolution"], adapter["VideoModeDescription"]),
                    RefreshRate = adapter["CurrentRefreshRate"] is uint refresh && refresh > 0
                        ? $"{refresh} Hz"
                        : "—",
                    VideoMode = adapter["VideoModeDescription"]?.ToString() ?? "—"
                });
            }
        }
        catch
        {
            // ignored
        }

        return adapters;
    }

    private static string FormatResolution(object? horizontal, object? vertical, object? fallback)
    {
        if (horizontal is uint width && vertical is uint height && width > 0 && height > 0)
        {
            return $"{width} × {height}";
        }

        return fallback?.ToString() ?? "—";
    }

    private static string FormatAdapterMemory(object? value)
    {
        try
        {
            return value switch
            {
                ulong bytes => SizeFormatter.FormatSize(bytes > long.MaxValue ? long.MaxValue : (long)bytes),
                uint bytes => SizeFormatter.FormatSize(bytes),
                _ => "—"
            };
        }
        catch
        {
            return "—";
        }
    }

    private static string FormatCacheSize(object? value)
    {
        if (value is null)
        {
            return "—";
        }

        if (double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var kilobytes) && kilobytes > 0)
        {
            return $"{kilobytes / 1024d:F1} MB";
        }

        return "—";
    }

    private static string DescribeArchitecture(object? value)
    {
        if (value is ushort code)
        {
            return code switch
            {
                0 => "x86",
                5 => "ARM",
                6 => "Itanium",
                9 => "x64",
                12 => "ARM64",
                _ => "Unknown"
            };
        }

        return "Unknown";
    }

    private static string FormatDriverDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "—";
        }

        try
        {
            var parsed = ManagementDateTimeConverter.ToDateTime(raw).ToLocalTime();
            return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch
        {
            return raw;
        }
    }

    private static string FormatInstallDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "—";
        }

        try
        {
            var parsed = ManagementDateTimeConverter.ToDateTime(raw).ToLocalTime();
            return parsed.ToString("MMM dd, yyyy", CultureInfo.CurrentCulture);
        }
        catch
        {
            return raw;
        }
    }

    private static WindowsInstallationDetailsViewModel QueryWindowsDetails()
    {
        var details = new WindowsInstallationDetailsViewModel();

        try
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hive.OpenSubKey(@"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion");
            if (key is not null)
            {
                details.Edition = key.GetValue("EditionID")?.ToString() ?? details.Edition;
                details.Version = key.GetValue("DisplayVersion")?.ToString() ?? key.GetValue("ReleaseId")?.ToString() ?? details.Version;
                details.Build = key.GetValue("CurrentBuild")?.ToString() ?? details.Build;
                details.ProductId = key.GetValue("ProductId")?.ToString() ?? details.ProductId;
                details.RegisteredOwner = key.GetValue("RegisteredOwner")?.ToString() ?? details.RegisteredOwner;
            }
        }
        catch
        {
            // ignored
        }

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT InstallDate FROM Win32_OperatingSystem");
            foreach (ManagementObject os in searcher.Get())
            {
                details.InstallDate = FormatInstallDate(os["InstallDate"]?.ToString());
                break;
            }
        }
        catch
        {
            // ignored
        }

        return details;
    }

    public sealed class ProcessorDetailsViewModel
    {
        public string Name { get; init; } = "Unknown";

        public string Manufacturer { get; init; } = "Unknown";

        public string Cores { get; init; } = "—";

        public string Threads { get; init; } = "—";

        public string BaseClock { get; init; } = "—";

        public string Architecture { get; init; } = "—";

        public string Socket { get; init; } = "—";

        public string L2Cache { get; init; } = "—";

        public string L3Cache { get; init; } = "—";

        public static ProcessorDetailsViewModel CreateDefault() => new();
    }

    public sealed class GraphicsAdapterViewModel
    {
        public string Name { get; init; } = "Unknown";

        public string Memory { get; init; } = "—";

        public string DriverVersion { get; init; } = "—";

        public string DriverDate { get; init; } = "—";

        public string Resolution { get; init; } = "—";

        public string RefreshRate { get; init; } = "—";

        public string VideoMode { get; init; } = "—";
    }

    public sealed class WindowsInstallationDetailsViewModel
    {
        public string Edition { get; set; } = "Windows";

        public string Version { get; set; } = "Unknown";

        public string Build { get; set; } = "Unknown";

        public string InstallDate { get; set; } = "—";

        public string RegisteredOwner { get; set; } = "Unknown";

        public string ProductId { get; set; } = "Unknown";

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Edition) && !string.IsNullOrWhiteSpace(Version) && !string.IsNullOrWhiteSpace(Build))
                {
                    return $"{Edition} {Version} (Build {Build})";
                }

                if (!string.IsNullOrWhiteSpace(Edition) && !string.IsNullOrWhiteSpace(Version))
                {
                    return $"{Edition} {Version}";
                }

                return Edition;
            }
        }
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
