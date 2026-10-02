using System.Text.Json;
using Microsoft.Win32;
using PabloDock.Models;

namespace PabloDock.Services;

public sealed class StartupSettingsService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "PabloDock";
    public const string StartupArgument = "--windows-startup";

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PabloDock", "settings.json");

    public StartupSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new StartupSettings();
        }

        using var stream = File.OpenRead(SettingsPath);
        return JsonSerializer.Deserialize<StartupSettings>(stream) ??
            throw new InvalidDataException("Startup settings are invalid.");
    }

    public void Save(StartupSettings settings)
    {
        if (settings.RestoreProfileAfterStartup &&
            (string.IsNullOrWhiteSpace(settings.ProfileName) || !settings.StartWithWindows))
        {
            throw new ArgumentException("Choose a profile and enable Start with Windows first.");
        }

        using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath) ??
            throw new InvalidOperationException("Could not open the current user's startup registry key.");
        var previousValue = runKey.GetValue(RunValueName) as string;
        try
        {
            if (settings.StartWithWindows)
            {
                var executablePath = Application.ExecutablePath;
                if (!File.Exists(executablePath) ||
                    !string.Equals(Path.GetExtension(executablePath), ".exe",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new FileNotFoundException("Could not find the PabloDock executable.",
                        executablePath);
                }

                runKey.SetValue(RunValueName,
                    $"\"{executablePath}\" {StartupArgument}", RegistryValueKind.String);
            }
            else
            {
                runKey.DeleteValue(RunValueName, false);
            }

            WriteSettings(settings);
        }
        catch
        {
            if (previousValue is null)
            {
                runKey.DeleteValue(RunValueName, false);
            }
            else
            {
                runKey.SetValue(RunValueName, previousValue, RegistryValueKind.String);
            }

            throw;
        }
    }

    public void UpdateProfileReference(StartupSettings settings) => WriteSettings(settings);

    private static void WriteSettings(StartupSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var temporaryPath = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, settings,
                    new JsonSerializerOptions { WriteIndented = true });
            }

            File.Move(temporaryPath, SettingsPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
