using Mehrak.Infrastructure.Auth;
using Mehrak.Infrastructure.Character;
using Mehrak.Infrastructure.CodeRedeem;
using Mehrak.Infrastructure.Documentation;
using Mehrak.Infrastructure.ReleaseNote;
using Mehrak.Infrastructure.Relic;
using Mehrak.Infrastructure.User;
using Mehrak.MigrationService;
using Mehrak.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

Console.WriteLine(builder.Configuration);

var connectionString = builder.Configuration.GetConnectionString("mehrakdb") ?? throw new InvalidOperationException("Postgres connection string is not configured.");

builder.Services.AddDbContext<DashboardAuthDbContext>((sp, options) =>
{
    options.UseNpgsql(connectionString);
    options.UseOpenIddict();
});
builder.Services.AddDbContext<CharacterDbContext>((sp, options) =>
    options.UseNpgsql(connectionString));
builder.Services.AddDbContext<UserDbContext>((sp, options) =>
    options.UseNpgsql(connectionString));
builder.Services.AddDbContext<CodeRedeemDbContext>((sp, options) =>
    options.UseNpgsql(connectionString));
builder.Services.AddDbContext<RelicDbContext>((sp, options) =>
    options.UseNpgsql(connectionString));
builder.Services.AddDbContext<DocumentationDbContext>((sp, options) =>
    options.UseNpgsql(connectionString));
builder.Services.AddDbContext<ReleaseNoteDbContext>((sp, options) =>
    options.UseNpgsql(connectionString));

builder.Services.AddHostedService<Worker>();

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(Worker.ActivitySourceName));

var host = builder.Build();
host.Run();
