using System.IO;
using Cleaner.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cleaner.App.ViewModels;

/// <summary>Карточка «C: свободно X из Y ГБ».</summary>
public partial class DriveViewModel(string root) : ObservableObject
{
    [ObservableProperty] private string text = "";
    [ObservableProperty] private double usedPercent;

    public long FreeBytes { get; private set; }

    public void Refresh()
    {
        try
        {
            var d = new DriveInfo(root);
            FreeBytes = d.AvailableFreeSpace;
            Text = $"{d.Name.TrimEnd('\\')} свободно {Format.Bytes(d.AvailableFreeSpace)} из {Format.Bytes(d.TotalSize)}";
            UsedPercent = 100.0 * (d.TotalSize - d.AvailableFreeSpace) / d.TotalSize;
        }
        catch (IOException)
        {
            Text = $"{root} недоступен";
        }
    }
}
