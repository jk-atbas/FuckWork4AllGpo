using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fuck4Work4allGpo.Infrastructure;

public sealed partial class CleanUpWorker(Work4AllLocator locator, ILogger<CleanUpWorker> logger) : BackgroundService
{
	private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(4);

	// Retry settings for locked files
	private const int MaxRetries = 5;
	private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

	protected override async Task ExecuteAsync(CancellationToken cancellationToken)
	{
		LogCleanUpWorkerStartedInterval(logger, CleanupInterval.TotalHours);

		// Run immediately on startup
		await RunCleanup(cancellationToken);

		// Then run periodically
		using var timer = new PeriodicTimer(CleanupInterval);

		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				if (await timer.WaitForNextTickAsync(cancellationToken))
				{
					await RunCleanup(cancellationToken);
				}
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception e)
			{
				logger.LogError(e, "Unexpected error in cleanup loop");
				// Continue the loop, don't crash the service
			}
		}

		logger.LogInformation("CleanupWorker stopped");
	}

	private async Task RunCleanup(CancellationToken ct)
	{
		logger.LogInformation("--- Starting periodic cleanup ---");

		var removedDirs = 0;

		// 1. Kill running work4all processes
		await KillWork4AllProcesses(ct);

		// 2. Remove installation directories
		var parentPaths = locator.FindAllParentPaths();
		foreach (var path in parentPaths)
		{
			if (ct.IsCancellationRequested)
			{
				break;
			}

			if (await TryDeleteDirectory(path, ct))
			{
				removedDirs++;
			}
		}

		// 3. Remove desktop shortcuts
		var shortcuts = locator.FindAllDesktopShortcuts();
		var removedShortcuts = shortcuts.TakeWhile(shortcut => !ct.IsCancellationRequested).Count(TryDeleteFile);

		LogCleanupCompleteRemovedDirsDirectoriesShortcutsShortcuts(logger, removedDirs, removedShortcuts);
	}

	private async Task KillWork4AllProcesses(CancellationToken ct)
	{
		try
		{
			var processes = System.Diagnostics.Process.GetProcessesByName("work4all");
			if (processes.Length == 0)
			{
				logger.LogDebug("No running work4all processes found");

				return;
			}

			foreach (var process in processes)
			{
				try
				{
					logger.LogWarning("Killing work4all process (PID: {PID})", process.Id);
					process.Kill(entireProcessTree: true);

					await process.WaitForExitAsync(ct);
					LogSuccessfullyKilledWork4AllProcessPid(logger, process.Id);
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "Failed to kill process PID: {PID}", process.Id);
				}
				finally
				{
					process.Dispose();
				}
			}

			// Give the OS a moment to release file handles
			await Task.Delay(TimeSpan.FromSeconds(2), ct);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error while killing work4all processes");
		}
	}

	private async Task<bool> TryDeleteDirectory(string path, CancellationToken ct)
	{
		for (int attempt = 1; attempt <= MaxRetries; attempt++)
		{
			try
			{
				if (!Directory.Exists(path))
				{
					LogDirectoryAlreadyGonePath(logger, path);

					return false;
				}

				// First, remove read-only attributes from all files
				RemoveReadOnlyAttributes(path);

				Directory.Delete(path, recursive: true);
				LogSuccessfullyDeletedDirectoryPath(logger, path);

				return true;
			}
			catch (IOException ex) when (attempt < MaxRetries)
			{
				logger.LogWarning(
					"Attempt {Attempt}/{Max} to delete {path} failed (IO): {Message}. Retrying...",
					attempt, MaxRetries, path, ex.Message);

				await Task.Delay(RetryDelay, ct);
			}
			catch (UnauthorizedAccessException ex) when (attempt < MaxRetries)
			{
				logger.LogWarning(
					"Attempt {Attempt}/{Max} to delete {path} failed (Access): {Message}. Retrying...",
					attempt, MaxRetries, path, ex.Message);

				await Task.Delay(RetryDelay, ct);
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Failed to delete directory: {path}", path);

				return false;
			}
		}

		return false;
	}

	private bool TryDeleteFile(string filePath)
	{
		try
		{
			if (!File.Exists(filePath))
			{
				return false;
			}

			File.SetAttributes(filePath, FileAttributes.Normal);
			File.Delete(filePath);
			LogSuccessfullyDeletedShortcutPath(logger, filePath);

			return true;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to delete shortcut: {path}", filePath);

			return false;
		}
	}

	private void RemoveReadOnlyAttributes(string directoryPath)
	{
		try
		{
			foreach (var file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
			{
				try
				{
					var attrs = File.GetAttributes(file);
					if (attrs.HasFlag(FileAttributes.ReadOnly))
					{
						File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
					}
				}
				catch { /* best effort */ }
			}
		}
		catch { /* best effort */ }
	}

	[LoggerMessage(LogLevel.Information, "--- Cleanup complete. Removed {dirs} directories, {shortcuts} shortcuts ---")]
	static partial void LogCleanupCompleteRemovedDirsDirectoriesShortcutsShortcuts(ILogger<CleanUpWorker> logger, int dirs, int shortcuts);

	[LoggerMessage(LogLevel.Information, "Successfully killed work4all process (pid: {pid})")]
	static partial void LogSuccessfullyKilledWork4AllProcessPid(ILogger<CleanUpWorker> logger, int pid);

	[LoggerMessage(LogLevel.Debug, "Directory already gone: {path}")]
	static partial void LogDirectoryAlreadyGonePath(ILogger<CleanUpWorker> logger, string path);

	[LoggerMessage(LogLevel.Information, "Successfully deleted directory: {path}")]
	static partial void LogSuccessfullyDeletedDirectoryPath(ILogger<CleanUpWorker> logger, string path);

	[LoggerMessage(LogLevel.Information, "Successfully deleted shortcut: {path}")]
	static partial void LogSuccessfullyDeletedShortcutPath(ILogger<CleanUpWorker> logger, string path);

	[LoggerMessage(LogLevel.Information, "CleanupWorker started. interval: {interval}h")]
	static partial void LogCleanUpWorkerStartedInterval(ILogger<CleanUpWorker> logger, double interval);
}
