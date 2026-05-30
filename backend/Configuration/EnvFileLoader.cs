namespace VirtualInfluencer.Backend.Configuration;

public static class EnvFileLoader
{
    public static void LoadIfPresent(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        var lines = File.ReadAllLines(filePath);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            value = RemoveWrappingQuotes(value);

            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    private static string RemoveWrappingQuotes(string value)
    {
        if (value.Length < 2)
        {
            return value;
        }

        var hasDoubleQuotes = value.StartsWith('"') && value.EndsWith('"');
        var hasSingleQuotes = value.StartsWith('\'') && value.EndsWith('\'');

        return hasDoubleQuotes || hasSingleQuotes
            ? value[1..^1]
            : value;
    }
}
