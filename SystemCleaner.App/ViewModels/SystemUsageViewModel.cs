using System;
using SystemCleaner.App.Services;

namespace SystemCleaner.App.ViewModels;

public sealed class SystemUsageViewModel : ObservableObject
{
    public SystemUsageViewModel(ComponentUsageViewModel cpu, ComponentUsageViewModel gpu)
    {
        Cpu = cpu ?? throw new ArgumentNullException(nameof(cpu));
        Gpu = gpu ?? throw new ArgumentNullException(nameof(gpu));
    }

    public ComponentUsageViewModel Cpu { get; }

    public ComponentUsageViewModel Gpu { get; }

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
    }

    public static SystemUsageViewModel CreateSample()
    {
        var cpu = new ComponentUsageViewModel("CPU");
        var gpu = new ComponentUsageViewModel("GPU");
        return new SystemUsageViewModel(cpu, gpu);
    }

    public sealed class ComponentUsageViewModel : ObservableObject
    {
        private double _frequencyMhz;
        private double _usagePercent;
        private double _memoryFrequencyMhz;
        private double _temperatureCelsius;
        private double _voltageMillivolts;
        private double _powerWatts;

        public ComponentUsageViewModel(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public string Name { get; }

        public double FrequencyMhz
        {
            get => _frequencyMhz;
            set => SetProperty(ref _frequencyMhz, value);
        }

        public double UsagePercent
        {
            get => _usagePercent;
            set => SetProperty(ref _usagePercent, value);
        }

        public double MemoryFrequencyMhz
        {
            get => _memoryFrequencyMhz;
            set => SetProperty(ref _memoryFrequencyMhz, value);
        }

        public double TemperatureCelsius
        {
            get => _temperatureCelsius;
            set => SetProperty(ref _temperatureCelsius, value);
        }

        public double VoltageMillivolts
        {
            get => _voltageMillivolts;
            set => SetProperty(ref _voltageMillivolts, value);
        }

        public double PowerWatts
        {
            get => _powerWatts;
            set => SetProperty(ref _powerWatts, value);
        }

        internal void UpdateFromSnapshot(HardwareSnapshot.ComponentSnapshot snapshot)
        {
            if (snapshot is null)
            {
                return;
            }

            FrequencyMhz = snapshot.CoreClockMhz ?? FrequencyMhz;
            UsagePercent = snapshot.UsagePercent ?? UsagePercent;
            MemoryFrequencyMhz = snapshot.MemoryClockMhz ?? MemoryFrequencyMhz;
            TemperatureCelsius = snapshot.TemperatureCelsius ?? TemperatureCelsius;
            VoltageMillivolts = snapshot.VoltageMillivolts ?? VoltageMillivolts;
            PowerWatts = snapshot.PowerWatts ?? PowerWatts;
        }
    }
}
