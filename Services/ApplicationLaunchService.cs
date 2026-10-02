using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using PabloDock.Models;
using PabloDock.Native;

namespace PabloDock.Services;

internal sealed record LaunchAttempt(bool Started, string? Target, string? Error);

internal static class ApplicationLaunchService
{
    internal static LaunchAttempt Launch(CapturedWindow window)
    {
        if (!string.IsNullOrWhiteSpace(window.CustomLaunchPath))
        {
            return LaunchExecutable(window.CustomLaunchPath, window.CustomLaunchArguments);
        }

        if (window.IsPackaged || !string.IsNullOrWhiteSpace(window.ApplicationUserModelId) ||
            IsWindowsAppsPath(window.ExecutablePath))
        {
            var aumid = window.ApplicationUserModelId ??
                ApplicationIdentityService.ResolveAumidFromPackagedPath(window.ExecutablePath);
            return ActivatePackagedApp(aumid);
        }

        var path = window.ExecutablePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return new LaunchAttempt(false, null, "No executable path was saved.");
        }

        var candidates = new List<(string Path, string? Arguments)>();
        var versionedRoot = GetVersionedInstallRoot(path);
        if (versionedRoot is not null)
        {
            if (window.ProcessName.Equals("Discord", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add((Path.Combine(versionedRoot, "Update.exe"),
                    "--processStart Discord.exe"));
            }

            try
            {
                var executableName = Path.GetFileName(path);
                candidates.AddRange(Directory.EnumerateDirectories(versionedRoot, "app-*")
                    .OrderByDescending(directory => Version.TryParse(
                        Path.GetFileName(directory)[4..], out var version)
                        ? version : new Version(0, 0))
                    .Select(directory => (Path.Combine(directory, executableName), (string?)null)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new LaunchAttempt(false, path,
                    $"Could not inspect the installation directory: {exception.Message}");
            }
        }

        candidates.Add((path, null));
        var errors = new List<string>();
        foreach (var candidate in candidates.Distinct())
        {
            if (!File.Exists(candidate.Path))
            {
                continue;
            }

            try
            {
                var startInfo = new ProcessStartInfo(candidate.Path)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(candidate.Path) ?? string.Empty,
                    Arguments = candidate.Arguments ?? string.Empty
                };
                using var process = Process.Start(startInfo);
                return new LaunchAttempt(true, candidate.Path, null);
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException
                                              or UnauthorizedAccessException or IOException)
            {
                errors.Add($"Process.Start threw {exception.GetType().Name} for '{candidate.Path}': {exception.Message}");
            }
        }

        return new LaunchAttempt(false, path, errors.Count > 0
            ? string.Join("; ", errors)
            : $"No usable executable was found for saved path '{path}'.");
    }

    private static LaunchAttempt LaunchExecutable(string path, string? arguments)
    {
        try
        {
            using var launcher = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty,
                Arguments = arguments ?? string.Empty
            });
            // The shell may hand off to another process (or return no process).
            // Window discovery determines when the application is ready.
            return new LaunchAttempt(true, path, null);
        }
        catch (Exception exception)
        {
            return new LaunchAttempt(false, path,
                $"Process.Start threw {exception.GetType().Name} for '{path}': {exception.Message}");
        }
    }

    private static LaunchAttempt ActivatePackagedApp(string? applicationUserModelId)
    {
        if (string.IsNullOrWhiteSpace(applicationUserModelId))
        {
            return new LaunchAttempt(false, null,
                "The packaged app has no saved AUMID. Save a new profile while the app is running.");
        }

        try
        {
            var manager = (IApplicationActivationManager)new ApplicationActivationManager();
            try
            {
                manager.ActivateApplication(applicationUserModelId, null, 0, out _);
                return new LaunchAttempt(true, $"AUMID: {applicationUserModelId}", null);
            }
            finally
            {
                Marshal.FinalReleaseComObject(manager);
            }
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException
                                          or UnauthorizedAccessException)
        {
            return new LaunchAttempt(false, $"AUMID: {applicationUserModelId}",
                $"Packaged app activation failed for '{applicationUserModelId}': {exception.Message}");
        }
    }

    private static bool IsWindowsAppsPath(string? path) =>
        path?.Contains("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase) == true;

    private static string? GetVersionedInstallRoot(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (directory is null ||
            !Path.GetFileName(directory).StartsWith("app-", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Directory.GetParent(directory)?.FullName;
    }
}
