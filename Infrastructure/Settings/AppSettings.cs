namespace Fuck4Work4allGpo.Infrastructure.Settings;

public sealed class AppSettings
{
	public Work4AllSettings Work4All { get; set; } = new Work4AllSettings
	{
		Directory = string.Empty,
		Executable = string.Empty,
		ShortcutExtension = string.Empty,
		ShortcutNamePattern = string.Empty,
	};
}
