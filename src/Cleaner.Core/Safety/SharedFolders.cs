namespace Cleaner.Core.Safety;

/// <summary>
/// Общие папки в Program Files, ProgramData и AppData, которыми пользуются многие программы или сама Windows.
/// Их нельзя считать «остатками» одной программы, даже если имя совпало с издателем.
/// </summary>
public static class SharedFolders
{
    public static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "Common Files", "Microsoft", "Microsoft Corporation", "Microsoft.NET", "Microsoft Office", "Microsoft Visual Studio",
        "Windows", "WindowsApps", "ModifiableWindowsApps", "Windows Defender", "Windows Defender Advanced Threat Protection",
        "Windows NT", "Windows Mail", "Windows Media Player", "Windows Photo Viewer", "Windows Sidebar", "Windows Security",
        "Windows Kits", "WindowsPowerShell", "Internet Explorer", "Reference Assemblies", "MSBuild", "dotnet",
        "Package Cache", "Packages", "Installer", "Uninstall Information", "Temp", "Programs", "Desktop", "Start Menu",
        "Application Data", "Documents", "regid.1991-06.com.microsoft", "USOShared", "USOPrivate", "SoftwareDistribution",
    };
}
