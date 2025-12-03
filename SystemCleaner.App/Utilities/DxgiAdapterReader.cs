using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.DXGI;

namespace SystemCleaner.App.Utilities;

internal static class DxgiAdapterReader
{
    internal sealed record AdapterInfo(string Name, string NormalizedName, ulong DedicatedVideoMemory);

    public static IReadOnlyList<AdapterInfo> GetAdapters()
    {
        var adapters = new List<AdapterInfo>();

        try
        {
            using var factory = new Factory1();
            var index = 0;

            while (true)
            {
                try
                {
                    using var adapter = factory.GetAdapter1(index);
                    var desc = adapter.Description1;
                    var name = desc.Description?.TrimEnd('\0') ?? $"Adapter {index}";
                    var normalized = NormalizeAdapterName(name);
                    var dedicatedBytes = (long)desc.DedicatedVideoMemory;
                    var dedicated = dedicatedBytes <= 0 ? 0UL : (ulong)dedicatedBytes;
                    adapters.Add(new AdapterInfo(name, normalized, dedicated));
                    index++;
                }
                catch (SharpDXException ex) when (ex.ResultCode == ResultCode.NotFound)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or COMException or NotSupportedException)
        {
            // DXGI not available; fall back to WMI-only data.
        }

        return adapters;
    }

    public static string NormalizeAdapterName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var span = name.AsSpan();
        var buffer = new char[span.Length];
        var position = 0;

        foreach (var ch in span)
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer[position++] = char.ToUpperInvariant(ch);
            }
            else if (char.IsWhiteSpace(ch))
            {
                buffer[position++] = ' ';
            }
        }

        return new string(buffer, 0, position);
    }
}
