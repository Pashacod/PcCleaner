using System.Runtime.InteropServices;

namespace Cleaner.Core.Sources;

/// <summary>Корзина всех дисков — через системные функции shell32.</summary>
public class RecycleBinSource : IJunkSource
{
    private const uint SHERB_NOCONFIRMATION = 0x1, SHERB_NOPROGRESSUI = 0x2, SHERB_NOSOUND = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint flags);

    public string Id => "recycle-bin";
    public string Title => "Корзина";
    public string Category => Categories.System;
    public string? Warning => null;
    public bool CheckedByDefault => true;

    public Task<ScanResult> ScanAsync(IProgress<long> progress, CancellationToken ct) => Task.Run(() =>
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        var size = SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : 0;
        progress.Report(size);
        IReadOnlyList<JunkItem> items = size > 0 ? [new JunkItem("Корзина", size, true, true)] : [];
        return new ScanResult(Id, items, size);
    }, ct);

    public Task<CleanResult> CleanAsync(ScanResult scan, CancellationToken ct) => Task.Run(() =>
    {
        var item = scan.Items.FirstOrDefault(i => i.Selected);
        if (item == null || ct.IsCancellationRequested) return CleanResult.Empty;
        var hr = SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
        return hr == 0
            ? new CleanResult(item.SizeBytes, 1, [])
            : new CleanResult(0, 0, [new SkippedItem("Корзина", $"ошибка очистки корзины (0x{hr:X8})")]);
    });
}
