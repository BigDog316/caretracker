using CareTrack.Client.Shared.Api;
using Microsoft.JSInterop;

namespace CareTrack.Client.Web;

/// <summary>
/// Browser download: hands the bytes to a small JS helper that creates a blob
/// URL and clicks a temporary link (see wwwroot/index.html).
/// </summary>
public sealed class BrowserFileSaver : IFileSaver
{
    private readonly IJSRuntime _js;

    public BrowserFileSaver(IJSRuntime js) => _js = js;

    public async Task SaveAsync(FileDownload file)
        => await _js.InvokeVoidAsync(
            "caretrackSaveFile", file.FileName, file.ContentType,
            Convert.ToBase64String(file.Content));
}
