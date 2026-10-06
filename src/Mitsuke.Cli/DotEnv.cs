namespace Mitsuke.Cli;

/// <summary>Loads KEY=VALUE lines from a local .env into the process environment. Real env vars win. Local dev only.</summary>
internal static class DotEnv
{
    public static void Load(string path)
    {
        if (!File.Exists(path)) return;

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim().Trim('"');
            if (Environment.GetEnvironmentVariable(key) is null)
                Environment.SetEnvironmentVariable(key, value);
        }
    }
}
