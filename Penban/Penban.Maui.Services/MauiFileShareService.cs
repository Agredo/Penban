using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;
using Penban.Services.Abstractions;

namespace Penban.Maui.Services;

/// <summary>
/// MAUI-backed <see cref="IFileShareService"/>: the platform share sheet, the platform file picker,
/// and a cache folder to write exports into.
/// </summary>
public class MauiFileShareService : IFileShareService
{
    /// <summary>Where an export is built before it is shared. The cache folder is the one place every platform lets the app write to without asking.</summary>
    public string CreateExportPath(string fileName)
    {
        var folder = Path.Combine(FileSystem.CacheDirectory, "export");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, fileName);
    }

    public Task ShareAsync(string filePath, string title)
        => Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = title,
            File = new ShareFile(filePath),
        });

    public async Task<string?> PickAsync(string title, string fileExtension)
    {
        var fileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            // Windows filters by extension. The other platforms can only filter by type, and a
            // .penban file has no registered one, so they are handed the generic type as well -
            // otherwise the file the user just shared would not even show up in the picker.
            [DevicePlatform.WinUI] = new[] { fileExtension },
            [DevicePlatform.Android] = new[] { "application/octet-stream", "*/*" },
            [DevicePlatform.iOS] = new[] { "public.data", "public.json" },
            [DevicePlatform.MacCatalyst] = new[] { "public.data", "public.json" },
        });

        var picked = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = title,
            FileTypes = fileTypes,
        });

        return picked?.FullPath;
    }
}
