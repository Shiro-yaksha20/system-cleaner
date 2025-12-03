using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SystemCleaner.Core.Uninstall;

public interface IUninstallerService
{
    Task<InstalledSoftwareSnapshot> GetInstalledSoftwareAsync(CancellationToken cancellationToken = default);

    Task<UninstallOperationResult> UninstallAsync(IEnumerable<InstalledApplication> targets, UninstallOptions options, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResidualItem>> FindResidualItemsAsync(IEnumerable<InstalledApplication> applications, CancellationToken cancellationToken = default);

    Task<ResidualCleanupResult> CleanupResidualItemsAsync(IEnumerable<ResidualItem> residualItems, CancellationToken cancellationToken = default);

    Task<ResidualCleanupResult> CleanupResidualItemsAsync(IEnumerable<ResidualItem> residualItems, ResidualCleanupOptions options, CancellationToken cancellationToken = default);

    Task<bool> RemoveBrowserExtensionAsync(BrowserExtensionInfo extension, CancellationToken cancellationToken = default);
}
