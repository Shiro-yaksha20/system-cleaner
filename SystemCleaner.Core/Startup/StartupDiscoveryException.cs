using System;

namespace SystemCleaner.Core.Startup;

public sealed class StartupDiscoveryException : Exception
{
    public StartupDiscoveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
