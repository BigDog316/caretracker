using CareTrack.Client.Mobile.Services;
using CareTrack.Client.Shared.Api;
using Microsoft.Extensions.Logging;

namespace CareTrack.Client.Mobile;

public static class MauiProgram
{
	/// <summary>
	/// Where the CareTrack API lives. Localhost works for Mac Catalyst and the
	/// iOS simulator; a device build needs the dev machine's LAN address (and
	/// the Android emulator would use 10.0.2.2).
	/// </summary>
	private const string ApiBaseUrl = "http://localhost:5210";

	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();

		// Shared CareTrack pages get the same services as the web host, with
		// platform-appropriate implementations.
		builder.Services.AddScoped(_ => new HttpClient
		{ BaseAddress = new Uri(ApiBaseUrl) });
		builder.Services.AddScoped<ITokenStore, SecureStorageTokenStore>();
		builder.Services.AddScoped<IFileSaver, ShareFileSaver>();
		builder.Services.AddScoped<CareTrackApi>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
