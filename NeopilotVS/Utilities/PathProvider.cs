using System;
using System.IO;

namespace NeopilotVS.Utilities;

public static class PathProvider
{
    public static string GetAppDataPath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, ".neopilot");
    }

    public static string GetLanguageServerFolder(string version)
    {
        return Path.Combine(GetAppDataPath(), $"language_server_v{version}");
    }

    public static string GetLanguageServerPath(string version, bool enterpriseMode)
    {
        string binaryName = enterpriseMode ? "language_server_windows_x64_enterprise.exe" : "language_server_windows_x64.exe";
        return Path.Combine(GetLanguageServerFolder(version), binaryName);
    }

    public static string GetDatabaseDirectory()
    {
        return Path.Combine(GetAppDataPath(), "database");
    }

    public static string GetAPIKeyPath()
    {
        return Path.Combine(GetAppDataPath(), "neopilot_api_key");
    }
}
