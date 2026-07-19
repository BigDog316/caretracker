using CareTrack.Client.Shared.Api;

namespace CareTrack.Client.Mobile.Services;

/// <summary>
/// Mobile "download": writes the bytes to the app cache and opens the system
/// share sheet so the user can save to Files, AirDrop, print, etc.
/// </summary>
public sealed class ShareFileSaver : IFileSaver
{
    public async Task SaveAsync(FileDownload file)
    {
        var path = Path.Combine(FileSystem.CacheDirectory, file.FileName);
        await File.WriteAllBytesAsync(path, file.Content);
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = file.FileName,
            File = new ShareFile(path, file.ContentType)
        });
    }
}
