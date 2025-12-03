using System;
using SystemCleaner.App.Services;

namespace SystemCleaner.App.ViewModels;

public sealed class SystemUsageViewModel : ObservableObject
{
    public SystemUsageViewModel(ComponentUsageViewModel cpu, ComponentUsageViewModel gpu, MemoryUsageViewModel memory)
    {
        Cpu = cpu ?? throw new ArgumentNullException(nameof(cpu));
        Gpu = gpu ?? throw new ArgumentNullException(nameof(gpu));
        Memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    public ComponentUsageViewModel Cpu { get; }

    public ComponentUsageViewModel Gpu { get; }

    public MemoryUsageViewModel Memory { get; }

    public void Update(HardwareSnapshot snapshot)
    {
        if (snapshot is null)
        {
            return;
        }

        if (snapshot.Cpu is not null)
        {
            Cpu.UpdateFromSnapshot(snapshot.Cpu);
        }

        if (snapshot.Gpu is not null)
        {
            Gpu.UpdateFromSnapshot(snapshot.Gpu);
        }

        if (snapshot.Memory is not null)
        {
            Memory.UpdateFromSnapshot(snapshot.Memory);
        }
    }

    public static SystemUsageViewModel CreateSample()
    {
        var cpu = new ComponentUsageViewModel("CPU");
        var gpu = new ComponentUsageViewModel("GPU");
        var memory = new MemoryUsageViewModel("Memory");
        return new SystemUsageViewModel(cpu, gpu, memory);
    }

    public sealed class ComponentUsageViewModel : ObservableObject
    {
        private double? _frequencyMhz;
        private double _usagePercent;
        private double? _memoryFrequencyMhz;
        private double? _temperatureCelsius;
        private double? _voltageMillivolts;
        private double? _powerWatts;
        private bool _hasTelemetry = true;

        public ComponentUsageViewModel(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public string Name { get; }

        public double? FrequencyMhz
        {
            get => _frequencyMhz;
            set => SetProperty(ref _frequencyMhz, value);
        }

        public double UsagePercent
        {
            get => _usagePercent;
            set => SetProperty(ref _usagePercent, value);
        }

        public double? MemoryFrequencyMhz
        {
            get => _memoryFrequencyMhz;
            set => SetProperty(ref _memoryFrequencyMhz, value);
        }

        public double? TemperatureCelsius
        {
            get => _temperatureCelsius;
            set => SetProperty(ref _temperatureCelsius, value);
        }

        public double? VoltageMillivolts
        {
            get => _voltageMillivolts;
            set => SetProperty(ref _voltageMillivolts, value);
        }

        public double? PowerWatts
        {
            get => _powerWatts;
            set => SetProperty(ref _powerWatts, value);
        }

        // Display properties that show "N/A" when data unavailable
        public string TemperatureDisplay => _temperatureCelsius.HasValue ? $"{_temperatureCelsius.Value:F1} °C" : "N/A";
        public string FrequencyDisplay => _frequencyMhz.HasValue ? $"{_frequencyMhz.Value:F0} MHz" : "N/A";
        public string MemoryFrequencyDisplay => _memoryFrequencyMhz.HasValue ? $"{_memoryFrequencyMhz.Value:F0} MHz" : "N/A";
        public string VoltageDisplay => _voltageMillivolts.HasValue ? $"{_voltageMillivolts.Value:F0} mV" : "N/A";
        public string PowerDisplay => _powerWatts.HasValue ? $"{_powerWatts.Value:F1} W" : "N/A";

        public bool HasTelemetry
        {
            get => _hasTelemetry;
            private set
            {
                if (SetProperty(ref _hasTelemetry, value))
                {
                    RaisePropertyChanged(nameof(TelemetryStatus));
                }
            }
        }

        public string TelemetryStatus => HasTelemetry
            ? string.Empty
            : "Sensor data unavailable. Run System Cleaner as administrator or enable monitoring in BIOS.";

        internal void UpdateFromSnapshot(HardwareSnapshot.ComponentSnapshot snapshot)
        {
            if (snapshot is null)
            {
                return;
            }

            FrequencyMhz = snapshot.CoreClockMhz;
            MemoryFrequencyMhz = snapshot.MemoryClockMhz;
            TemperatureCelsius = snapshot.TemperatureCelsius;
            VoltageMillivolts = snapshot.VoltageMillivolts;
            PowerWatts = snapshot.PowerWatts;
            UsagePercent = snapshot.UsagePercent ?? UsagePercent;
            HasTelemetry = snapshot.HasTelemetry;

            NotifyDisplayProperties();
        }

        private void NotifyDisplayProperties()
        {
            RaisePropertyChanged(nameof(FrequencyDisplay));
            RaisePropertyChanged(nameof(MemoryFrequencyDisplay));
            RaisePropertyChanged(nameof(TemperatureDisplay));
            RaisePropertyChanged(nameof(VoltageDisplay));
            RaisePropertyChanged(nameof(PowerDisplay));
        }
    }

    public sealed class MemoryUsageViewModel : ObservableObject
    {
        private double _usagePercent;
        private string _usedDisplay = "—";
        private string _availableDisplay = "—";
        private string _totalDisplay = "—";
        private bool _hasTelemetry;

        public MemoryUsageViewModel(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public string Name { get; }

        public double UsagePercent
        {
            get => _usagePercent;
            set => SetProperty(ref _usagePercent, value);
        }

        public string UsedDisplay
        {
            get => _usedDisplay;
            private set => SetProperty(ref _usedDisplay, value);
        }

        public string AvailableDisplay
        {
            get => _availableDisplay;
            private set => SetProperty(ref _availableDisplay, value);
        }

        public string TotalDisplay
        {
            get => _totalDisplay;
            private set => SetProperty(ref _totalDisplay, value);
        }

        public bool HasTelemetry
        {
            get => _hasTelemetry;
            private set => SetProperty(ref _hasTelemetry, value);
        }

        internal void UpdateFromSnapshot(HardwareSnapshot.MemorySnapshot snapshot)
        {
            if (snapshot is null)
            {
                return;
            }

            if (snapshot.UsagePercent.HasValue)
            {
                UsagePercent = snapshot.UsagePercent.Value;
            }

            if (snapshot.UsedGigabytes.HasValue)
            {
                UsedDisplay = $"{snapshot.UsedGigabytes.Value:F1} GB";
            }

            if (snapshot.AvailableGigabytes.HasValue)
            {
                AvailableDisplay = $"{snapshot.AvailableGigabytes.Value:F1} GB";
            }

            if (snapshot.TotalGigabytes.HasValue)
            {
                TotalDisplay = $"{snapshot.TotalGigabytes.Value:F1} GB";
            }

            HasTelemetry = snapshot.HasTelemetry;
        }
    }
}
