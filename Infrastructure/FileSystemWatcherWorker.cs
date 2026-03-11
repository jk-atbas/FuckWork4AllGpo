using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fuck4Work4allGpo.Infrastructure;

public sealed partial class FileSystemWatcherWorker(
	Work4AllLocator locator,
	ILogger<FileSystemWatcherWorker> logger) : BackgroundService
{
	private readonly List<FileSystemWatcher> watchers = [];

	// Debounce: avoid reacting multiple times to the same installation
	private readonly Dictionary<string, DateTime> recentEvents = [];
	private static readonly TimeSpan DebounceInterval = TimeSpan.FromSeconds(10);
	private static readonly TimeSpan DeletionDelay = TimeSpan.FromSeconds(5);

	protected override async Task ExecuteAsync(CancellationToken cancellationToken)
	{
		logger.LogInformation("FileSystemWatcherWorker starting...");

		SetupDirectoryWatchers();
		SetupDesktopWatchers();

		LogFilesystemWatcherWorkerRunningWithCountWatchers(logger, watchers.Count);

		// Keep the service alive
		try
		{
			await Task.Delay(Timeout.Infinite, cancellationToken);
		}
		catch (OperationCanceledException)
		{
			// Expected on shutdown
		}

		// Cleanup
		foreach (var watcher in watchers)
		{
			watcher.EnableRaisingEvents = false;
			watcher.Dispose();
		}

		watchers.Clear();

		logger.LogInformation("FileSystemWatcherWorker stopped");
	}

	private void SetupDirectoryWatchers()
	{
		if (locator.GetWatchPath() is not { } path)
		{
			return;
		}

		try
		{
			var watcher = new FileSystemWatcher(path)
			{
				NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName,
				IncludeSubdirectories = true,
				EnableRaisingEvents = true
			};

			watcher.Created += OnFileOrDirectoryCreated;
			watcher.Renamed += OnFileOrDirectoryRenamed;
			watcher.Error += OnWatcherError;

			watchers.Add(watcher);
			LogWatchingDirectoryPath(logger, path);
		}
		catch (Exception e)
		{
			logger.LogError(e, "Failed to create watcher for: {path}", path);
		}
	}

	private void SetupDesktopWatchers()
	{
		List<string> desktopPaths = locator.GetDesktopWatchPaths();

		foreach (var path in desktopPaths)
		{
			try
			{
				var watcher = new FileSystemWatcher(path)
				{
					NotifyFilter = NotifyFilters.FileName,
					Filter = "*.lnk",
					IncludeSubdirectories = false,
					EnableRaisingEvents = true
				};

				watcher.Created += OnShortcutCreated;
				watcher.Renamed += OnShortcutRenamed;
				watcher.Error += OnWatcherError;

				watchers.Add(watcher);
				LogWatchingDesktopForShortcutsPath(logger, path);
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Failed to create desktop watcher for: {path}", path);
			}
		}
	}

	private void OnFileOrDirectoryCreated(object sender, FileSystemEventArgs e)
	{
		if (IsWork4AllPath(e.FullPath))
		{
			HandleWork4AllDetected(e.FullPath, "Created");
		}
	}

	private void OnFileOrDirectoryRenamed(object sender, RenamedEventArgs e)
	{
		if (IsWork4AllPath(e.FullPath))
		{
			HandleWork4AllDetected(e.FullPath, "Renamed");
		}
	}

	private void OnShortcutCreated(object sender, FileSystemEventArgs e)
	{
		HandleShortcutDetected(e.FullPath, "Created");
	}

	private void OnShortcutRenamed(object sender, RenamedEventArgs e)
	{
		HandleShortcutDetected(e.FullPath, "Renamed");
	}

	private bool IsWork4AllPath(string path)
	{
		return path.Contains("work4all GmbH", StringComparison.OrdinalIgnoreCase)
			   || path.Contains("work4all\\", StringComparison.OrdinalIgnoreCase)
			   || Path.GetFileName(path).Equals("work4all", StringComparison.OrdinalIgnoreCase)
			   || Path.GetFileName(path).Equals("work4all.exe", StringComparison.OrdinalIgnoreCase);
	}

	private void HandleWork4AllDetected(string path, string eventType)
	{
		if (!ShouldProcess(path))
		{
			return;
		}

		logger.LogWarning(
			"work4all installation detected! Event: {Event}, path: {path}",
			eventType, path);

		// Find the "work4all GmbH" root to delete
		var targetPath = FindWork4AllRoot(path);

		// Fire and forget the deletion (with a small delay for file locks to release)
		_ = Task.Run(async () =>
		{
			try
			{
				// Wait a bit for the installation to finish writing files
				await Task.Delay(DeletionDelay);

				// Kill any running processes first
				await KillWork4AllProcessesAsync();

				// Another short delay after killing processes
				await Task.Delay(TimeSpan.FromSeconds(2));

				if (Directory.Exists(targetPath))
				{
					RemoveReadOnlyAttributes(targetPath);
					Directory.Delete(targetPath, recursive: true);
					LogSuccessfullyRemovedWork4AllDirectoryPath(logger, targetPath);
				}
			}
			catch (Exception e)
			{
				logger.LogError(e,
					"Failed to remove work4all directory: {path}. Will be retried in next cleanup cycle.",
					targetPath);
			}
		});
	}

	private void HandleShortcutDetected(string shortcutPath, string eventType)
	{
		// Check if the shortcut name or content relates to work4all
		var fileName = Path.GetFileNameWithoutExtension(shortcutPath);

		if (!fileName.Contains("work4all", StringComparison.OrdinalIgnoreCase))
		{
			// Check the link target via raw bytes
			if (!IsWork4AllShortcutContent(shortcutPath))
			{
				return;
			}
		}

		if (!ShouldProcess(shortcutPath))
		{
			return;
		}

		logger.LogWarning(
			"work4all shortcut detected! Event: {Event}, path: {path}",
			eventType, shortcutPath);

		_ = Task.Run(async () =>
		{
			try
			{
				await Task.Delay(TimeSpan.FromSeconds(2));

				if (File.Exists(shortcutPath))
				{
					File.SetAttributes(shortcutPath, FileAttributes.Normal);
					File.Delete(shortcutPath);
					LogSuccessfullyRemovedShortcutPath(logger, shortcutPath);
				}
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Failed to remove shortcut: {path}", shortcutPath);
			}
		});
	}

	private bool IsWork4AllShortcutContent(string lnkPath)
	{
		try
		{
			if (!File.Exists(lnkPath))
			{
				return false;
			}

			var bytes = File.ReadAllBytes(lnkPath);
			var content = System.Text.Encoding.Unicode.GetString(bytes)
						  + System.Text.Encoding.ASCII.GetString(bytes);

			return content.Contains("work4all", StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	private string FindWork4AllRoot(string path)
	{
		// Walk up the path to find "work4all GmbH" directory
		var current = path;
		while (!string.IsNullOrEmpty(current))
		{
			var dirName = Path.GetFileName(current);
			if (dirName.Equals("work4all GmbH", StringComparison.OrdinalIgnoreCase))
			{
				return current;
			}

			current = Path.GetDirectoryName(current);
		}

		// Fallback: return the path as-is
		return path;
	}

	/// <summary>
	/// Simple debounce mechanism: don't process the same path
	/// if we already handled it within the debounce interval.
	/// </summary>
	private bool ShouldProcess(string path)
	{
		var normalizedPath = path.ToLowerInvariant();
		var now = DateTime.UtcNow;

		lock (recentEvents)
		{
			// Clean up old entries
			var expired = recentEvents
				.Where(kvp => now - kvp.Value > DebounceInterval)
				.Select(kvp => kvp.Key)
				.ToList();

			foreach (var key in expired)
			{
				recentEvents.Remove(key);
			}

			// Check and record
			if (recentEvents.TryAdd(normalizedPath, now))
			{
				return true;
			}

			LogDebouncedEventForPath(logger, path);
			return false;

		}
	}

	private async Task KillWork4AllProcessesAsync()
	{
		try
		{
			var processes = System.Diagnostics.Process.GetProcessesByName("work4all");
			foreach (var process in processes)
			{
				try
				{
					logger.LogWarning("Killing work4all process (PID: {PID})", process.Id);
					process.Kill(entireProcessTree: true);
					await process.WaitForExitAsync();
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
		}
		catch (Exception e)
		{
			logger.LogError(e, "Error killing work4all processes");
		}
	}

	private void RemoveReadOnlyAttributes(string directoryPath)
	{
		try
		{
			foreach (var file in Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories))
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

	private void OnWatcherError(object sender, ErrorEventArgs e)
	{
		logger.LogError(e.GetException(), "FileSystemWatcher error");

		// Try to restart the watcher
		if (sender is not FileSystemWatcher watcher)
		{
			return;
		}

		try
		{
			watcher.EnableRaisingEvents = false;
			watcher.EnableRaisingEvents = true;
			LogFilesystemWatcherRestartedForPath(logger, watcher.Path);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to restart FileSystemWatcher for: {path}", watcher.Path);
		}
	}

	[LoggerMessage(LogLevel.Information, "Watching directory: {path}")]
	static partial void LogWatchingDirectoryPath(ILogger<FileSystemWatcherWorker> logger, string path);

	[LoggerMessage(LogLevel.Information, "Watching desktop for shortcuts: {path}")]
	static partial void LogWatchingDesktopForShortcutsPath(ILogger<FileSystemWatcherWorker> logger, string path);

	[LoggerMessage(LogLevel.Information, "Successfully removed work4all directory: {path}")]
	static partial void LogSuccessfullyRemovedWork4AllDirectoryPath(ILogger<FileSystemWatcherWorker> logger, string path);

	[LoggerMessage(LogLevel.Information, "Successfully removed shortcut: {path}")]
	static partial void LogSuccessfullyRemovedShortcutPath(ILogger<FileSystemWatcherWorker> logger, string path);

	[LoggerMessage(LogLevel.Debug, "Debounced event for: {path}")]
	static partial void LogDebouncedEventForPath(ILogger<FileSystemWatcherWorker> logger, string path);

	[LoggerMessage(LogLevel.Information, "FileSystemWatcher restarted for: {path}")]
	static partial void LogFilesystemWatcherRestartedForPath(ILogger<FileSystemWatcherWorker> logger, string path);

	[LoggerMessage(LogLevel.Information, "FileSystemWatcherWorker running with {count} watchers")]
	static partial void LogFilesystemWatcherWorkerRunningWithCountWatchers(ILogger<FileSystemWatcherWorker> logger, int count);
}
