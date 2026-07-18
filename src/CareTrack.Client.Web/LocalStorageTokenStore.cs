using System.Text.Json;
using CareTrack.Client.Shared.Api;
using Microsoft.JSInterop;

namespace CareTrack.Client.Web;

/// <summary>
/// Browser-host token persistence in localStorage. Dev-grade like the API's
/// raw refresh tokens: acceptable for the SPA today, revisit alongside the
/// server-side hardening pass (cookie or hashed-token options).
/// </summary>
public sealed class LocalStorageTokenStore : ITokenStore
{
    private const string Key = "caretrack.auth";
    private readonly IJSRuntime _js;

    public LocalStorageTokenStore(IJSRuntime js) => _js = js;

    public async Task<StoredTokens?> LoadAsync()
    {
        var raw = await _js.InvokeAsync<string?>("localStorage.getItem", Key);
        if (string.IsNullOrEmpty(raw)) return null;
        try
        {
            return JsonSerializer.Deserialize<StoredTokens>(raw);
        }
        catch (JsonException)
        {
            return null; // corrupt entry: treat as signed out
        }
    }

    public async Task SaveAsync(StoredTokens tokens)
        => await _js.InvokeVoidAsync(
            "localStorage.setItem", Key, JsonSerializer.Serialize(tokens));

    public async Task ClearAsync()
        => await _js.InvokeVoidAsync("localStorage.removeItem", Key);
}
