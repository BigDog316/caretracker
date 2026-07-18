using CareTrack.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCareTrackPersistence(builder.Configuration);
builder.Services.AddCareTrackAuth(builder.Configuration);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Browser clients (Blazor WASM dev server, deployed web app). Origins come
// from config so production lists only the real client origin.
var clientOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
if (clientOrigins.Length > 0)
{
    builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
        .WithOrigins(clientOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()));
}

var app = builder.Build();

if (clientOrigins.Length > 0)
    app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Exposed so integration tests can reference the entry point via WebApplicationFactory.
public partial class Program { }
