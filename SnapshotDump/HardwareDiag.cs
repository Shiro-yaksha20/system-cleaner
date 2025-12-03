using System;
using System.IO;
using System.Text;
using LibreHardwareMonitor.Hardware;

namespace SnapshotDump;

public static class HardwareDiag
{
    public static void DumpAllSensors(string outputPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Hardware Sensor Dump - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine(new string('=', 80));

        var computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
            IsNetworkEnabled = false,
            IsStorageEnabled = true
        };

        computer.Open();

        foreach (var hardware in computer.Hardware)
        {
            DumpHardware(hardware, sb, 0);
        }

        computer.Close();

        File.WriteAllText(outputPath, sb.ToString());
        Console.WriteLine($"Sensor dump written to: {outputPath}");
    }

    private static void DumpHardware(IHardware hardware, StringBuilder sb, int indent)
    {
        var prefix = new string(' ', indent * 2);

        hardware.Update();

        sb.AppendLine();
        sb.AppendLine($"{prefix}[{hardware.HardwareType}] {hardware.Name}");
        sb.AppendLine($"{prefix}  Identifier: {hardware.Identifier}");
        sb.AppendLine($"{prefix}  Sensors ({hardware.Sensors.Length}):");

        foreach (var sensor in hardware.Sensors)
        {
            var valueStr = sensor.Value.HasValue ? sensor.Value.Value.ToString("F2") : "null";
            sb.AppendLine($"{prefix}    [{sensor.SensorType}] {sensor.Name} = {valueStr}");
        }

        foreach (var sub in hardware.SubHardware)
        {
            DumpHardware(sub, sb, indent + 1);
        }
    }
}
