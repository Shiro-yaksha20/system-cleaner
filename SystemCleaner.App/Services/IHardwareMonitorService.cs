using System;

namespace SystemCleaner.App.Services;

public interface IHardwareMonitorService : IDisposable
{
    event EventHandler<HardwareSnapshotEventArgs>? SnapshotAvailable;

    void Start();

    void Stop();
}
