using System.Text.Json;

namespace PabloDock.Services;

public sealed class ProcessExclusionService
{
    private static string ExclusionsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PabloDock", "exclusions.json");

    public IReadOnlyList<string> Load()
    {
        if (!File.Exists(ExclusionsPath))
        {
            return [];
        }

        using var stream = File.OpenRead(ExclusionsPath);
        return JsonSerializer.Deserialize<string[]>(stream) ??
            throw new InvalidDataException("Process exclusions are invalid.");
    }

    public void Save(IEnumerable<string> processNames)
    {
        var names = processNames.Select(Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(ExclusionsPath)!);
        var temporaryPath = ExclusionsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, names,
                    new JsonSerializerOptions { WriteIndented = true });
            }

            File.Move(temporaryPath, ExclusionsPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static string Normalize(string processName)
    {
        var name = processName.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        if (name.Length == 0 || name.IndexOfAny(['\\', '/', ':']) >= 0)
        {
            throw new ArgumentException("Enter a process name, such as Discord or Discord.exe.");
        }

        return name;
    }
}
