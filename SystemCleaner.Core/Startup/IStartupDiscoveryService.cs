using System.Threading;
using System.Threading.Tasks;
using SystemCleaner.Core.Models;

namespace SystemCleaner.Core.Startup;

public interface IStartupDiscoveryService
{
    Task<StartupDiscoveryResult> GetStartupEntriesAsync(CancellationToken cancellationToken = default);

    Task SetStartupEntryEnabledAsync(StartupEntry entry, bool isEnabled, CancellationToken cancellationToken = default);

    Task<bool?> GetStartupEntryApprovalStateAsync(StartupEntry entry, CancellationToken cancellationToken = default);
}
