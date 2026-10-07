namespace Mitsuke.Api;

/// <summary>
/// Local development only: loads the repo's .env (found by walking up from the working directory) into the
/// environment so the API shares the CLI's settings. Real environment variables win. Never used in Azure.
/// </summary>
internal static class DotEnv
{
    public static void LoadNearest()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, ".env");
            if (!File.Exists(path)) continue;

            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                var eq = line.IndexOf('=', StringComparison.Ordinal);
                if (line.StartsWith('#') || eq <= 0) continue;
                var key = line[..eq].Trim();
                if (Environment.GetEnvironmentVariable(key) is null)
                    Environment.SetEnvironmentVariable(key, line[(eq + 1)..].Trim().Trim('"'));
            }
            return;
        }
    }
}
