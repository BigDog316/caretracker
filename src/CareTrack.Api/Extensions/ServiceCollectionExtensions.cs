using System.Text;
using CareTrack.Api.Auth;
using CareTrack.Application;
using CareTrack.Domain;
using CareTrack.Infrastructure;
using CareTrack.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace CareTrack.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCareTrackPersistence(
        this IServiceCollection services, IConfiguration config)
    {
        var cs = config.GetConnectionString("CareTrackDb")
                 ?? "Host=localhost;Database=caretrack;Username=postgres;Password=postgres";

        services.AddDbContext<CareTrackDbContext>(o => o.UseNpgsql(cs));
        services.AddScoped<IAccessGrantStore, EfAccessGrantStore>();
        services.AddScoped<ICareDataRepository, EfCareDataRepository>();
        services.AddScoped<CareProfileAccessService>();

        // Feature services (Providers + Appointments + Notes slice).
        services.AddScoped<CareProfileService>();
        services.AddScoped<ProviderService>();
        services.AddScoped<AppointmentService>();
        services.AddScoped<NoteService>();
        services.AddScoped<FollowUpReminderService>();

        // Documents + Cards slice.
        services.AddScoped<DocumentService>();
        services.AddScoped<CardService>();

        // School (IEP/504) + Agencies slice.
        services.AddScoped<AgencyService>();
        services.AddScoped<SchoolPlanService>();

        var storeOptions = new CareTrack.Infrastructure.Storage.LocalDiskDocumentStoreOptions();
        config.GetSection(CareTrack.Infrastructure.Storage.LocalDiskDocumentStoreOptions.SectionName)
            .Bind(storeOptions);
        services.AddSingleton(storeOptions);
        services.AddScoped<IDocumentStore,
            CareTrack.Infrastructure.Storage.LocalDiskDocumentStore>();

        // Calendar sync + clock. Google sync activates when OAuth credentials
        // are configured (user-secrets/env: GoogleCalendar:ClientId/ClientSecret);
        // otherwise events simply don't sync. Apple arrives with the MAUI client.
        services.Configure<CareTrack.Infrastructure.Calendar.GoogleCalendarOptions>(
            config.GetSection(CareTrack.Infrastructure.Calendar.GoogleCalendarOptions.SectionName));
        services.AddScoped<CareTrack.Infrastructure.Calendar.IGoogleCalendarConnectionStore,
            CareTrack.Infrastructure.Calendar.EfGoogleCalendarConnectionStore>();
        services.AddMemoryCache();
        services.AddHttpClient();
        var googleCal = config
            .GetSection(CareTrack.Infrastructure.Calendar.GoogleCalendarOptions.SectionName)
            .Get<CareTrack.Infrastructure.Calendar.GoogleCalendarOptions>();
        if (googleCal?.IsConfigured == true)
            services.AddHttpClient<ICalendarSync,
                CareTrack.Infrastructure.Calendar.GoogleCalendarSync>();
        else
            services.AddScoped<ICalendarSync, NoOpCalendarSync>();
        services.AddSingleton<IClock, SystemClock>();

        // "How did it go?" prompt delivery goes out as Web Push notifications
        // when VAPID keys are configured; otherwise prompts just log (dev
        // fallback), so the sweep behaves identically either way.
        services.Configure<FollowUpReminderOptions>(
            config.GetSection(FollowUpReminderOptions.SectionName));
        services.Configure<CareTrack.Infrastructure.Push.WebPushOptions>(
            config.GetSection(CareTrack.Infrastructure.Push.WebPushOptions.SectionName));
        services.AddScoped<CareTrack.Infrastructure.Push.IPushSubscriptionStore,
            CareTrack.Infrastructure.Push.EfPushSubscriptionStore>();
        var webPush = config
            .GetSection(CareTrack.Infrastructure.Push.WebPushOptions.SectionName)
            .Get<CareTrack.Infrastructure.Push.WebPushOptions>();
        if (webPush?.IsConfigured == true)
        {
            services.AddSingleton<CareTrack.Infrastructure.Push.IWebPushSender,
                CareTrack.Infrastructure.Push.VapidWebPushSender>();
            services.AddScoped<IReminderDelivery,
                CareTrack.Infrastructure.Push.WebPushReminderDelivery>();
        }
        else
        {
            services.AddScoped<IReminderDelivery,
                CareTrack.Infrastructure.Reminders.LoggingReminderDelivery>();
        }
        services.AddScoped(sp => new FollowUpReminderDispatcher(
            sp.GetRequiredService<ICareDataRepository>(),
            sp.GetRequiredService<IAccessGrantStore>(),
            sp.GetRequiredService<IReminderDelivery>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<FollowUpReminderOptions>>()
                .Value.RepromptInterval));
        services.AddHostedService<CareTrack.Api.Reminders.FollowUpReminderHostedService>();
        return services;
    }

    public static IServiceCollection AddCareTrackAuth(
        this IServiceCollection services, IConfiguration config)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        services.Configure<JwtOptions>(config.GetSection(JwtOptions.SectionName));
        var jwt = config.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
                  ?? new JwtOptions();

        // ASP.NET Core Identity backed by the CareTrack DbContext.
        services
            .AddIdentityCore<AppUser>(o =>
            {
                o.Password.RequiredLength = 10;
                o.Password.RequireNonAlphanumeric = false;
                o.User.RequireUniqueEmail = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<CareTrackDbContext>();

        // Auth application services + JWT issuer.
        services.AddScoped<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddScoped<IAuthService, IdentityAuthService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });

        services.AddScoped<IAuthorizationHandler, CareProfileAccessHandler>();

        services.AddAuthorization(o =>
        {
            o.AddPolicy(AccessPolicies.Viewer, p => p.AddRequirements(
                new CareProfileAccessRequirement(AccessRole.Viewer)));
            o.AddPolicy(AccessPolicies.Editor, p => p.AddRequirements(
                new CareProfileAccessRequirement(AccessRole.Editor)));
            o.AddPolicy(AccessPolicies.Owner, p => p.AddRequirements(
                new CareProfileAccessRequirement(AccessRole.Owner)));
        });

        return services;
    }
}
