using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Mrp.Api;
using Mrp.Platform.Endpoints;
using Mrp.SharedKernel.Web;

// Server-side formatting and parsing never depend on the host's culture (e.g. Thai Buddhist calendar);
// localisation happens in the web and mobile clients.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);
var migrate = args.Contains("migrate", StringComparer.OrdinalIgnoreCase);

// The migrator connects as the schema owner; the API connects as a role that is subject to RLS.
var connectionName = migrate ? "Migrator" : "App";
var connectionString = builder.Configuration.GetConnectionString(connectionName)
    ?? throw new InvalidOperationException($"Connection string '{connectionName}' is not configured.");

builder.Services.AddMrpModules(builder.Configuration, connectionString);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    // Numbers are JSON numbers only; this also keeps the OpenAPI schema free of "number | string".
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddOpenApi(options => options.AddDocumentTransformer<BearerSecurityTransformer>());
builder.Services.AddHealthChecks();

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
    policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
}));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var permitLimit = builder.Configuration.GetValue("RateLimit:AuthPerMinute", 20);
    options.AddPolicy(PlatformEndpoints.AuthRateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    // Cloud Run / Cloudflare terminate TLS; trust their forwarded headers.
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

if (migrate)
{
    await DatabaseMigrator.RunAsync(app.Services, app.Configuration, app.Logger);
    return;
}

await DatabaseMigrator.EnsureSafeRoleAsync(app.Services, app.Environment, app.Logger);

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/healthz").AllowAnonymous();
app.MapOpenApi().AllowAnonymous();

var api = app.MapGroup("/api/v1");
api.MapMrpModules();

app.Run();

/// <summary>Entry point marker for integration tests.</summary>
public partial class Program;
