using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using SystemCleaner.App.ViewModels;
using SystemCleaner.Core.Uninstall;
using System.Runtime.Versioning;
using SystemCleaner.App.Services;

internal sealed class Program
{
	[SupportedOSPlatform("windows")]
	public static async Task Main()
	{
		var exitCodeSource = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
		var thread = new Thread(() => RunOnStaThread(exitCodeSource))
		{
			Name = "SnapshotDump STA",
			IsBackground = false
		};
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();

		var exitCode = await exitCodeSource.Task.ConfigureAwait(false);
		Environment.ExitCode = exitCode;
	}

	private static void RunOnStaThread(TaskCompletionSource<int> exitCodeSource)
	{
		try
		{
			SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
			var dispatcher = Dispatcher.CurrentDispatcher;
			dispatcher.InvokeAsync(async () =>
			{
				var exitCode = 0;
				try
				{
					await DumpAsync().ConfigureAwait(true);
				}
				catch (Exception ex)
				{
					Console.WriteLine("Diagnostics failed: " + ex);
					exitCode = 1;
				}
				finally
				{
					exitCodeSource.TrySetResult(exitCode);
					dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
				}
			});
			Dispatcher.Run();
		}
		catch (Exception ex)
		{
			Console.WriteLine("Dispatcher setup failed: " + ex);
			exitCodeSource.TrySetResult(1);
		}
	}

	private static async Task DumpAsync()
	{
		var service = new UninstallerService();
		Console.WriteLine("Collecting snapshot...");
		var snapshot = await service.GetInstalledSoftwareAsync().ConfigureAwait(true);
		Console.WriteLine($"Service returned: {snapshot.Applications.Count}");
		Console.WriteLine($"Empty names: {snapshot.Applications.Count(app => string.IsNullOrWhiteSpace(app.Name))}");
		Console.WriteLine($"Empty publishers: {snapshot.Applications.Count(app => string.IsNullOrWhiteSpace(app.Publisher))}");
		Console.WriteLine("Creating view model...");

		var confirmationService = new UserConfirmationService { RequireConfirmation = false };
		var vm = new UninstallerViewModel(service, confirmationService);
		Console.WriteLine("View model created.");
		await vm.InitializeAsync().ConfigureAwait(true);
		Console.WriteLine($"ViewModel applications: {vm.Applications.Count}");
		Console.WriteLine($"Filtered view count: {vm.ApplicationsView.Cast<object>().Count()}");
		Console.WriteLine($"Current status: {vm.StatusMessage}");
		Console.WriteLine($"ShowWindowsAppsOnly: {vm.ShowWindowsAppsOnly}");
		Console.WriteLine($"Search text: '{vm.SearchText}'");
	}
}
