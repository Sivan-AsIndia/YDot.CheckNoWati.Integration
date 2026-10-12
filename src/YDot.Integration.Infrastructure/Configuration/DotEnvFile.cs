namespace YDot.Integration.Infrastructure.Configuration;

/// <summary>
/// Loads KEY=VALUE pairs from a .env file into environment variables, so secrets (CheckNumber API
/// key, WATI token, database password) live in one git-ignored file instead of appsettings.json.
///
/// Keys use the .NET configuration convention: a double underscore is a section separator, so
/// <c>CheckNumber__ApiKey=...</c> becomes the setting CheckNumber:ApiKey.
///
/// RULES, kept deliberately simple and predictable:
///   - a variable that is ALREADY set in the environment wins (Docker, a Windows service, a CI job
///     can always override the file);
///   - blank lines and lines starting with # are ignored, as is a leading "export ";
///   - a value may be wrapped in single or double quotes, which are removed; nothing is expanded
///     (a "$" is a literal "$", unlike in docker compose).
/// </summary>
public static class DotEnvFile
{
    /// <summary>
    /// Looks for ".env" in each start folder and up to five parent folders, loads the first one
    /// found and returns its path (null when there is none).
    /// </summary>
    public static string? Load(params string[] startFolders)
    {
        foreach (var start in startFolders.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var folder = new DirectoryInfo(Path.GetFullPath(start));
            for (var depth = 0; folder is not null && depth <= 5; depth++, folder = folder.Parent)
            {
                var candidate = Path.Combine(folder.FullName, ".env");
                if (File.Exists(candidate))
                {
                    Apply(candidate);
                    return candidate;
                }
            }
        }

        return null;
    }

    private static void Apply(string path)
    {
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line["export ".Length..].TrimStart();
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();

            if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
