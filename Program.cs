using Fuck4Work4allGpo.Infrastructure;
using Fuck4Work4allGpo.Infrastructure.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Fuck4Work4allGpo;

internal class Program
{
	private static async Task Main(string[] args)
	{
		Log.Logger = new LoggerConfiguration()
			.MinimumLevel.Information()
			.WriteTo.Console()
			.WriteTo.File(
				path: Path.Combine(AppContext.BaseDirectory, "logs", "work4allblocker-.log"),
				rollingInterval: RollingInterval.Day,
				retainedFileCountLimit: 30,
				outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
			.CreateLogger();

		try
		{
			Log.Information("=== Work4allBlocker Service starting ===");

			var host = Host.CreateDefaultBuilder(args)
				.UseSerilog()
				.ConfigureServices((context, services) =>
				{
					services.Configure<AppSettings>(context.Configuration);
					services.AddSingleton<Work4AllLocator>();
					services.AddHostedService<CleanUpWorker>();
					services.AddHostedService<FileSystemWatcherWorker>();
				})
				.Build();

			await host.RunAsync();
		}
		catch (Exception ex)
		{
			Log.Fatal(ex, "Service terminated unexpectedly");
		}
		finally
		{
			Log.Information("=== Work4allBlocker Service stopped ===");
			await Log.CloseAndFlushAsync();
		}
	}
}
