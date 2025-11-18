using System.Collections.Generic;

namespace SystemCleaner.Core.Uninstall;

public sealed class UninstallOperationResult
{
    public UninstallOperationResult(int successful, int failed, IReadOnlyList<string> messages)
    {
        Successful = successful;
        Failed = failed;
        Messages = messages;
    }

    public int Successful { get; }

    public int Failed { get; }

    public IReadOnlyList<string> Messages { get; }
}
