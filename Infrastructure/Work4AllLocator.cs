using Fuck4Work4allGpo.Infrastructure.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fuck4Work4allGpo.Infrastructure;

/// <summary>
/// Locates work4all installation directories and desktop shortcuts
/// across all user profiles on the system.
/// </summary>
public sealed partial class Work4AllLocator(IOptionsMonitor<AppSettings> appSettingsMonitor, ILogger<Work4AllLocator> logger)
{
	private const string Work4AllRootFolder = "work4all GmbH";

	private static readonly EnumerationOptions EnumerationOptions = new()
	{
		RecurseSubdirectories = true,
		IgnoreInaccessible = true,
	};

	public string[] FindInstallationPaths()
	{
		Work4AllSettings currentSettings = appSettingsMonitor.CurrentValue.Work4All;

		try
		{
			string directory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(currentSettings.Directory));

			if (!Directory.Exists(directory))
			{
				logger.LogWarning("Invalid Work4all path was entered");

				return [];
			}

			var foundFiles = Directory.EnumerateFiles(
				currentSettings.Directory,
				currentSettings.Executable,
				EnumerationOptions)
				.ToArray();

			if (foundFiles.Length == 0)
			{
				logger.LogWarning("No executables were found under {dir}", directory);

				return [];
			}

			LogFoundCountInstallationsUnderDir(logger, foundFiles.Length, currentSettings.Directory);

			return foundFiles;
		}
		catch (Exception e)
		{
			logger.LogError(e, "Error scanning for work4all installations");
		}

		return [];
	}

	/// <summary>
	/// Returns all parent directories (work4all GmbH) to clean up completely.
	/// </summary>
	public string[] FindAllParentPaths()
	{
		Work4AllSettings currentSettings = appSettingsMonitor.CurrentValue.Work4All;

		try
		{
			string directory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(currentSettings.Directory));

			if (!Directory.Exists(directory))
			{
				return [];
			}

			var result = Directory.EnumerateDirectories(
				directory,
				Work4AllRootFolder,
				EnumerationOptions)
				.ToArray();

			if (result.Length == 0)
			{
				logger.LogWarning("No parent paths found");

				return [];
			}

			LogFoundCountParentPathsUnderDir(logger, result.Length, directory);

			return result;
		}
		catch (Exception e)
		{
			logger.LogError(e, "Error scanning for work4all parent directories");
		}

		return [];
	}

	/// <summary>
	/// Finds all work4all desktop shortcuts across all user profiles
	/// and the Public Desktop.
	/// </summary>
	public List<string> FindAllDesktopShortcuts()
	{
		var results = new List<string>();

		try
		{
			// Check Public Desktop
			string publicDesktop = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));

			ScanDesktopForShortcuts(publicDesktop, results);

			string userDir = Path.GetFullPath(
				Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));

			ScanDesktopForShortcuts(userDir, results);
		}
		catch (Exception e)
		{
			logger.LogError(e, "Error scanning for desktop shortcuts");
		}

		return results;
	}

	/// <summary>
	/// Returns all directories to watch with FileSystemWatcher.
	/// </summary>
	public string? GetWatchPath()
	{
		Work4AllSettings currentSettings = appSettingsMonitor.CurrentValue.Work4All;

		try
		{
			string directory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(currentSettings.Directory));

			return !Directory.Exists(directory) ? null : directory;
		}
		catch (Exception e)
		{
			logger.LogError(e, "Error determining watch paths");
		}

		return null;
	}

	/// <summary>
	/// Returns all desktop directories to watch for new shortcut creation.
	/// </summary>
	public List<string> GetDesktopWatchPaths()
	{
		var results = new List<string>();

		try
		{
			// Public Desktop
			string publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

			if (Directory.Exists(publicDesktop))
			{
				results.Add(publicDesktop);
			}

			// Each user's Desktop
			var userDir = Path.GetFullPath(
				Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));

			if (Directory.Exists(userDir))
			{
				results.Add(userDir);
			}

			return results;
		}
		catch (Exception e)
		{
			logger.LogError(e, "Error determining desktop watch paths");
		}

		return results;
	}

	private void ScanDesktopForShortcuts(string desktopPath, List<string> results)
	{
		Work4AllSettings currentSettings = appSettingsMonitor.CurrentValue.Work4All;

		if (!Directory.Exists(desktopPath))
		{
			return;
		}

		try
		{
			// Find .lnk files matching work4all pattern
			var shortcuts = Directory.EnumerateFiles(
				desktopPath,
				currentSettings.ShortcutNamePattern + currentSettings.ShortcutExtension);

			foreach (var shortcut in shortcuts)
			{
				results.Add(shortcut);
				LogFoundWork4AllShortcutPath(logger, shortcut);
			}

			// Also check all .lnk files for targets pointing to work4all paths
			foreach (var lnkFile in Directory.EnumerateFiles(desktopPath, "*" + currentSettings.ShortcutExtension))
			{
				if (results.Contains(lnkFile))
				{
					continue;
				}

				if (!IsWork4AllShortcut(lnkFile))
				{
					continue;
				}

				results.Add(lnkFile);
				LogFoundShortcutTargetingWork4AllPath(logger, lnkFile);
			}
		}
		catch (Exception e)
		{
			logger.LogWarning(e, "Error scanning desktop: {path}", desktopPath);
		}
	}

	private bool IsWork4AllShortcut(string lnkPath)
	{
		try
		{
			// Simple heuristic: read the file as bytes and look for the target string
			var bytes = File.ReadAllBytes(lnkPath);
			var content = System.Text.Encoding.Unicode.GetString(bytes)
						  + System.Text.Encoding.ASCII.GetString(bytes);

			return content.Contains("work4all", StringComparison.OrdinalIgnoreCase)
				   && (content.Contains(appSettingsMonitor.CurrentValue.Work4All.Executable, StringComparison.OrdinalIgnoreCase)
					   || content.Contains("work4all GmbH", StringComparison.OrdinalIgnoreCase));
		}
		catch
		{
			return false;
		}
	}

	[LoggerMessage(LogLevel.Information, "Found {count} installations under {dir}")]
	static partial void LogFoundCountInstallationsUnderDir(ILogger<Work4AllLocator> logger, int count, string dir);

	[LoggerMessage(LogLevel.Information, "Found {count} parent paths under {dir}")]
	static partial void LogFoundCountParentPathsUnderDir(ILogger<Work4AllLocator> logger, int count, string dir);

	[LoggerMessage(LogLevel.Information, "Found work4all shortcut: {path}")]
	static partial void LogFoundWork4AllShortcutPath(ILogger<Work4AllLocator> logger, string path);

	[LoggerMessage(LogLevel.Information, "Found shortcut targeting work4all: {path}")]
	static partial void LogFoundShortcutTargetingWork4AllPath(ILogger<Work4AllLocator> logger, string path);
}
