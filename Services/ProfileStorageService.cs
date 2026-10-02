using System.Text.Json;
using System.Text.Json.Serialization;
using PabloDock.Models;

namespace PabloDock.Services;

public sealed class ProfileStorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string ProfilesDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PabloDock", "Profiles");

    public IReadOnlyList<string> ListProfileNames()
    {
        if (!Directory.Exists(ProfilesDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(ProfilesDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null)
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public LayoutProfile Load(string name)
    {
        ValidateName(name);
        var path = Path.Combine(ProfilesDirectory, name + ".json");
        using var stream = File.OpenRead(path);
        var profile = JsonSerializer.Deserialize<LayoutProfile>(stream, JsonOptions);
        if (profile?.Windows is null)
        {
            throw new InvalidDataException("The selected profile does not contain a valid window list.");
        }

        return profile;
    }

    public string Save(LayoutProfile profile)
    {
        var name = profile.Name.Trim();
        ValidateName(name);

        Directory.CreateDirectory(ProfilesDirectory);

        var path = Path.Combine(ProfilesDirectory, name + ".json");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, profile, JsonOptions);
        return path;
    }

    public void Update(LayoutProfile profile)
    {
        ValidateName(profile.Name);
        var path = Path.Combine(ProfilesDirectory, profile.Name + ".json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The selected profile no longer exists.", path);
        }

        var temporaryPath = Path.Combine(ProfilesDirectory,
            $".{profile.Name}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, profile, JsonOptions);
            }

            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public void Rename(string currentName, string newName)
    {
        ValidateName(currentName);
        newName = newName.Trim();
        ValidateName(newName);
        if (string.Equals(currentName, newName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Choose a different profile name.");
        }

        var profile = Load(currentName);
        var newPath = Save(profile with { Name = newName });
        try
        {
            Delete(currentName);
        }
        catch
        {
            File.Delete(newPath);
            throw;
        }
    }

    public void Delete(string name)
    {
        ValidateName(name);
        var path = Path.Combine(ProfilesDirectory, name + ".json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The selected profile no longer exists.", path);
        }

        File.Delete(path);
    }

    private static void ValidateName(string name)
    {
        if (name.Length == 0 || name.Length > 100 ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.EndsWith('.') || name.EndsWith(' '))
        {
            throw new ArgumentException("Use a profile name of 1–100 characters without file name symbols or a trailing dot or space.");
        }

        var stem = name.Split('.')[0];
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            (stem.Length == 4 && stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) && stem[3] is >= '1' and <= '9') ||
            (stem.Length == 4 && stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase) && stem[3] is >= '1' and <= '9'))
        {
            throw new ArgumentException("That profile name is reserved by Windows. Choose another name.");
        }
    }
}
