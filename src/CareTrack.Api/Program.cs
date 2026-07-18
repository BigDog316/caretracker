using CareTrack.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCareTrackPersistence(builder.Configuration);
builder.Services.AddCareTrackAuth(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

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
