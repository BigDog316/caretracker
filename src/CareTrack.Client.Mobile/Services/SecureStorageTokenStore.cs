using System.Text.Json;
using CareTrack.Client.Shared.Api;

namespace CareTrack.Client.Mobile.Services;

/// <summary>
/// Token persistence in the platform keychain via MAUI SecureStorage — the
/// mobile counterpart of the web host's localStorage store, but actually
/// encrypted at rest by the OS.
/// </summary>
public sealed class SecureStorageTokenStore : ITokenStore
{
    private const string Key = "caretrack.auth";

    public async Task<StoredTokens?> LoadAsync()
    {
        var raw = await SecureStorage.Default.GetAsync(Key);
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

    public Task SaveAsync(StoredTokens tokens)
        => SecureStorage.Default.SetAsync(Key, JsonSerializer.Serialize(tokens));

    public Task ClearAsync()
    {
        SecureStorage.Default.Remove(Key);
        return Task.CompletedTask;
    }
}
