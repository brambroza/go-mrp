using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mrp.Api;
using Mrp.Platform.Application;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Mrp.Integration.Tests.Infrastructure;

/// <summary>
/// Starts PostgreSQL 17 in a container, creates the owner/app roles, runs the real migrator and hosts
/// the API in-process connected as the RLS-restricted application role.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly string _ownerPassword = RandomSecret();
    private readonly string _appPassword = RandomSecret();
    private readonly string _signingKey = RandomSecret() + RandomSecret();
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("postgres")
        .Build();

    private WebApplicationFactory<Program>? _factory;

    /// <summary>JSON options matching the API (camelCase, enums as strings).</summary>
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Connection string of the application role (subject to RLS).</summary>
    public string AppConnectionString { get; private set; } = string.Empty;

    /// <summary>Connection string of the schema owner.</summary>
    public string OwnerConnectionString { get; private set; } = string.Empty;

    /// <summary>Host services, for resolving scoped services in tests.</summary>
    public IServiceProvider Services => _factory!.Services;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using (var admin = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await admin.OpenAsync();
            await ExecuteAsync(admin, $"CREATE ROLE mrp_owner LOGIN PASSWORD '{_ownerPassword}' NOSUPERUSER NOBYPASSRLS");
            await ExecuteAsync(admin, $"CREATE ROLE mrp_app LOGIN PASSWORD '{_appPassword}' NOSUPERUSER NOBYPASSRLS");
            await ExecuteAsync(admin, "CREATE DATABASE mrp OWNER mrp_owner");
        }

        var builder = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = "mrp" };
        builder.Username = "mrp_owner";
        builder.Password = _ownerPassword;
        OwnerConnectionString = builder.ConnectionString;
        builder.Username = "mrp_app";
        builder.Password = _appPassword;
        AppConnectionString = builder.ConnectionString;

        var settings = new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = _signingKey,
            ["Database:AppRole"] = "mrp_app",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var migratorServices = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(configuration)
            .AddMrpModules(configuration, OwnerConnectionString)
            .BuildServiceProvider();
        await using (migratorServices)
        {
            await DatabaseMigrator.RunAsync(migratorServices, configuration, NullLogger.Instance);
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Testing");
            host.UseSetting("ConnectionStrings:App", AppConnectionString);
            host.UseSetting("Jwt:SigningKey", _signingKey);
            host.UseSetting("RateLimit:AuthPerMinute", "100000");
            host.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        });
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }

    /// <summary>Anonymous HTTP client.</summary>
    public HttpClient CreateClient() => _factory!.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Signs up a new tenant and returns a client authenticated as its owner.</summary>
    public async Task<TenantSession> CreateTenantAsync(string? plan = "Pro")
    {
        var code = "t" + Guid.NewGuid().ToString("N")[..12];
        var password = "Passw0rd-" + Guid.NewGuid().ToString("N")[..8];
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/signup", new
        {
            companyCode = code,
            companyName = $"Company {code}",
            email = $"owner@{code}.test",
            password,
            displayName = "Owner",
            plan,
        });
        await response.ShouldBeAsync(System.Net.HttpStatusCode.OK);
        var tokens = (await response.Content.ReadFromJsonAsync<TokenResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return new TenantSession(this, client, code, $"owner@{code}.test", password, tokens);
    }

    /// <summary>Signs in and returns an authenticated client.</summary>
    public async Task<(HttpClient Client, TokenResponse Tokens)> LoginAsync(string companyCode, string userName, string password)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { companyCode, userName, password });
        await response.ShouldBeAsync(System.Net.HttpStatusCode.OK);
        var tokens = (await response.Content.ReadFromJsonAsync<TokenResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return (client, tokens);
    }

    private static string RandomSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>An authenticated owner of a freshly created tenant.</summary>
public sealed record TenantSession(ApiFixture Fixture, HttpClient Client, string CompanyCode, string UserName, string Password, TokenResponse Tokens)
{
    /// <summary>Tenant id.</summary>
    public Guid TenantId => Tokens.User.TenantId;

    /// <summary>Owner user id.</summary>
    public Guid UserId => Tokens.User.Id;

    /// <summary>Creates a role with permissions and a user holding it; returns the user's client.</summary>
    public async Task<(HttpClient Client, Guid UserId, Guid RoleId)> CreateUserAsync(string name, params string[] permissions)
    {
        var roleResponse = await Client.PostAsJsonAsync("/api/v1/roles", new { name = $"role-{name}", permissions });
        await roleResponse.ShouldBeAsync(System.Net.HttpStatusCode.OK);
        var role = (await roleResponse.Content.ReadFromJsonAsync<RoleDto>(ApiFixture.Json))!;
        var password = "Passw0rd-" + Guid.NewGuid().ToString("N")[..8];
        var userResponse = await Client.PostAsJsonAsync("/api/v1/users", new
        {
            userName = name,
            email = $"{name}@{CompanyCode}.test",
            displayName = name,
            password,
            roleIds = new[] { role.Id },
        });
        await userResponse.ShouldBeAsync(System.Net.HttpStatusCode.OK);
        var user = (await userResponse.Content.ReadFromJsonAsync<UserDto>(ApiFixture.Json))!;
        var (client, _) = await Fixture.LoginAsync(CompanyCode, name, password);
        return (client, user.Id, role.Id);
    }
}

/// <summary>Shares one database container across the test classes of the collection.</summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    /// <summary>Collection name.</summary>
    public const string Name = "api";
}

/// <summary>Assertion helpers for HTTP responses.</summary>
public static class HttpAssertions
{
    /// <summary>Asserts the status code and includes the response body in the failure message.</summary>
    public static async Task ShouldBeAsync(this HttpResponseMessage response, System.Net.HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Expected {(int)expected} but got {(int)response.StatusCode}: {body}");
        }
    }

    /// <summary>Reads the <c>code</c> extension of a problem response.</summary>
    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>Reads the response body as <typeparamref name="T"/>.</summary>
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(ApiFixture.Json))!;
}
