using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Mrp.Integration.Tests.Infrastructure;
using Mrp.Platform.Application;
using Mrp.Platform.Domain;
using Mrp.SharedKernel.Domain;
using Mrp.SharedKernel.Persistence;
using Mrp.SharedKernel.Tenancy;
using Mrp.SharedKernel.Web;
using Npgsql;

namespace Mrp.Integration.Tests;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(ApiFixture fixture)
{
    [Fact]
    public async Task Signup_creates_tenant_with_owner_holding_all_permissions()
    {
        var session = await fixture.CreateTenantAsync("Starter");

        var profile = await (await session.Client.GetAsync("/api/v1/auth/me")).ReadAsync<UserProfile>();

        Assert.Equal(session.CompanyCode, profile.TenantSlug);
        Assert.Equal(TenantPlan.Starter, profile.Plan);
        Assert.Contains(AppRole.OwnerRoleName, profile.Roles);
        Assert.Contains("*", profile.Permissions);
    }

    [Fact]
    public async Task Signup_with_taken_company_code_is_rejected()
    {
        var session = await fixture.CreateTenantAsync();

        var response = await fixture.CreateClient().PostAsJsonAsync("/api/v1/auth/signup", new
        {
            companyCode = session.CompanyCode,
            companyName = "Other",
            email = "x@other.test",
            password = "Passw0rd-1234",
            displayName = "X",
        });

        await response.ShouldBeAsync(HttpStatusCode.Conflict);
        Assert.Equal("platform.tenant.slug_taken", await response.ProblemCodeAsync());
    }

    [Theory]
    [InlineData("ab", "Company", "a@b.test", "Passw0rd-1", "companyCode")]
    [InlineData("good-code", "Company", "not-an-email", "Passw0rd-1", "email")]
    [InlineData("good-code", "Company", "a@b.test", "short", "password")]
    [InlineData("bad code!", "Company", "a@b.test", "Passw0rd-1", "companyCode")]
    public async Task Signup_validates_input(string code, string name, string email, string password, string invalidField)
    {
        var response = await fixture.CreateClient().PostAsJsonAsync("/api/v1/auth/signup", new
        {
            companyCode = code, companyName = name, email, password, displayName = "X",
        });

        await response.ShouldBeAsync(HttpStatusCode.BadRequest);
        Assert.Contains($"\"{invalidField}\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_with_wrong_password_or_unknown_company_returns_same_error()
    {
        var session = await fixture.CreateTenantAsync();
        var client = fixture.CreateClient();

        var wrongPassword = await client.PostAsJsonAsync("/api/v1/auth/login", new { companyCode = session.CompanyCode, userName = session.UserName, password = "Wrong-pass-1" });
        var unknownCompany = await client.PostAsJsonAsync("/api/v1/auth/login", new { companyCode = "no-such-company", userName = session.UserName, password = session.Password });

        await wrongPassword.ShouldBeAsync(HttpStatusCode.Unauthorized);
        await unknownCompany.ShouldBeAsync(HttpStatusCode.Unauthorized);
        Assert.Equal(await wrongPassword.ProblemCodeAsync(), await unknownCompany.ProblemCodeAsync());
    }

    [Fact]
    public async Task Login_is_locked_after_five_failures()
    {
        var session = await fixture.CreateTenantAsync();
        var client = fixture.CreateClient();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await client.PostAsJsonAsync("/api/v1/auth/login", new { companyCode = session.CompanyCode, userName = session.UserName, password = "Wrong-pass-1" });
        }

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { companyCode = session.CompanyCode, userName = session.UserName, password = session.Password });

        await response.ShouldBeAsync(HttpStatusCode.Locked);
    }

    [Fact]
    public async Task Refresh_rotates_token_and_reuse_revokes_the_session()
    {
        var session = await fixture.CreateTenantAsync();
        var client = fixture.CreateClient();

        var first = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = session.Tokens.RefreshToken });
        await first.ShouldBeAsync(HttpStatusCode.OK);
        var rotated = await first.ReadAsync<TokenResponse>();
        Assert.NotEqual(session.Tokens.RefreshToken, rotated.RefreshToken);

        var reuse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = session.Tokens.RefreshToken });
        await reuse.ShouldBeAsync(HttpStatusCode.Unauthorized);

        var afterReuse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = rotated.RefreshToken });
        await afterReuse.ShouldBeAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_revokes_refresh_token()
    {
        var session = await fixture.CreateTenantAsync();
        var client = fixture.CreateClient();

        await (await client.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = session.Tokens.RefreshToken })).ShouldBeAsync(HttpStatusCode.NoContent);

        var refresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = session.Tokens.RefreshToken });
        await refresh.ShouldBeAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Protected_endpoint_requires_token()
    {
        var response = await fixture.CreateClient().GetAsync("/api/v1/auth/me");

        await response.ShouldBeAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task User_without_permission_is_forbidden_and_package_limit_is_enforced()
    {
        var session = await fixture.CreateTenantAsync("Starter");
        var (userClient, _, roleId) = await session.CreateUserAsync("clerk", Permissions.MastersRead);

        await (await userClient.GetAsync("/api/v1/users")).ShouldBeAsync(HttpStatusCode.Forbidden);

        // Starter allows 5 active users: owner + clerk + 3 more.
        for (var index = 0; index < 3; index++)
        {
            var created = await session.Client.PostAsJsonAsync("/api/v1/users", new
            {
                userName = $"user{index}", email = $"user{index}@{session.CompanyCode}.test", displayName = "U", password = "Passw0rd-1234", roleIds = new[] { roleId },
            });
            await created.ShouldBeAsync(HttpStatusCode.OK);
        }

        var overLimit = await session.Client.PostAsJsonAsync("/api/v1/users", new
        {
            userName = "user-extra", email = $"extra@{session.CompanyCode}.test", displayName = "U", password = "Passw0rd-1234", roleIds = new[] { roleId },
        });
        await overLimit.ShouldBeAsync(HttpStatusCode.Conflict);
        Assert.Equal("platform.users.limit_reached", await overLimit.ProblemCodeAsync());
    }

    [Fact]
    public async Task Last_owner_cannot_be_deactivated()
    {
        var session = await fixture.CreateTenantAsync();
        var roles = await (await session.Client.GetAsync("/api/v1/roles")).ReadAsync<List<RoleDto>>();
        var owner = roles.Single(r => r.IsSystem);

        var response = await session.Client.PutAsJsonAsync($"/api/v1/users/{session.UserId}", new
        {
            email = session.UserName, displayName = "Owner", roleIds = new[] { owner.Id }, isActive = false,
        });

        await response.ShouldBeAsync(HttpStatusCode.Conflict);
        Assert.Equal("platform.users.last_owner", await response.ProblemCodeAsync());
    }
}

[Collection(ApiCollection.Name)]
public sealed class TenantIsolationTests(ApiFixture fixture)
{
    [Fact]
    public async Task Same_user_name_can_exist_in_two_tenants_and_lists_are_separate()
    {
        var a = await fixture.CreateTenantAsync();
        var b = await fixture.CreateTenantAsync();
        await a.CreateUserAsync("somchai", Permissions.MastersRead);
        await b.CreateUserAsync("somchai", Permissions.MastersRead);

        var usersOfA = await (await a.Client.GetAsync("/api/v1/users")).ReadAsync<PagedResult<UserDto>>();
        var usersOfB = await (await b.Client.GetAsync("/api/v1/users")).ReadAsync<PagedResult<UserDto>>();

        Assert.Equal(2, usersOfA.Total);
        Assert.Equal(2, usersOfB.Total);
        Assert.Empty(usersOfA.Items.Select(u => u.Id).Intersect(usersOfB.Items.Select(u => u.Id)));
    }

    [Fact]
    public async Task Updating_a_user_of_another_tenant_returns_not_found()
    {
        var a = await fixture.CreateTenantAsync();
        var b = await fixture.CreateTenantAsync();
        var roles = await (await a.Client.GetAsync("/api/v1/roles")).ReadAsync<List<RoleDto>>();

        var response = await a.Client.PutAsJsonAsync($"/api/v1/users/{b.UserId}", new
        {
            email = "hacked@a.test", displayName = "Hacked", roleIds = new[] { roles[0].Id }, isActive = true,
        });

        await response.ShouldBeAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Database_enforces_isolation_even_without_application_filters()
    {
        var a = await fixture.CreateTenantAsync();
        var b = await fixture.CreateTenantAsync();
        await using var connection = new NpgsqlConnection(fixture.AppConnectionString);
        await connection.OpenAsync();

        Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM platform.users"));

        await ExecuteAsync(connection, $"SELECT set_config('app.tenant_id', '{a.TenantId}', false)");
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM platform.users"));
        Assert.Equal(0L, await ScalarAsync(connection, $"SELECT count(*) FROM platform.users WHERE tenant_id = '{b.TenantId}'"));

        var updated = await ExecuteAsync(connection, $"UPDATE platform.users SET display_name = 'x' WHERE tenant_id = '{b.TenantId}'");
        Assert.Equal(0, updated);

        var insert = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection,
            $"INSERT INTO platform.tenant_settings (id, tenant_id, key, value, created_at) VALUES (gen_random_uuid(), '{b.TenantId}', 'k', 'true', now())"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, insert.SqlState);
    }

    [Fact]
    public async Task Every_table_with_tenant_id_has_forced_row_level_security_and_a_policy()
    {
        await using var connection = new NpgsqlConnection(fixture.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.table_schema || '.' || c.table_name
            FROM information_schema.columns c
            JOIN pg_namespace n ON n.nspname = c.table_schema
            JOIN pg_class k ON k.relnamespace = n.oid AND k.relname = c.table_name AND k.relkind = 'r'
            WHERE c.column_name = 'tenant_id'
              AND (NOT k.relrowsecurity OR NOT k.relforcerowsecurity
                   OR NOT EXISTS (SELECT 1 FROM pg_policies p WHERE p.schemaname = c.table_schema AND p.tablename = c.table_name))
            """;
        var unprotected = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            unprotected.Add(reader.GetString(0));
        }

        Assert.Empty(unprotected);
    }

    [Fact]
    public async Task Application_role_cannot_bypass_row_level_security()
    {
        await using var connection = new NpgsqlConnection(fixture.AppConnectionString);
        await connection.OpenAsync();

        Assert.Equal(false, await ScalarAsync(connection, "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user"));
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private static async Task<int> ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteNonQueryAsync();
    }
}

[Collection(ApiCollection.Name)]
public sealed class NumberingTests(ApiFixture fixture)
{
    [Fact]
    public async Task Concurrent_requests_get_gapless_unique_numbers_per_tenant()
    {
        var a = await fixture.CreateTenantAsync();
        var b = await fixture.CreateTenantAsync();
        var date = new DateTimeOffset(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);

        var numbersOfA = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => NextAsync(a.TenantId, "PO", date)));
        var numbersOfB = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => NextAsync(b.TenantId, "PO", date)));

        Assert.Equal(
            Enumerable.Range(1, 50).Select(n => $"PO-2610-{n:0000}"),
            numbersOfA.Order(StringComparer.Ordinal));
        Assert.Equal(
            Enumerable.Range(1, 5).Select(n => $"PO-2610-{n:0000}"),
            numbersOfB.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Period_uses_tenant_time_zone_and_restarts_each_month()
    {
        var session = await fixture.CreateTenantAsync();

        // 2026-10-31 18:30 UTC is already 1 November in Asia/Bangkok.
        var october = await NextAsync(session.TenantId, "GR", new DateTimeOffset(2026, 10, 31, 16, 0, 0, TimeSpan.Zero));
        var november = await NextAsync(session.TenantId, "GR", new DateTimeOffset(2026, 10, 31, 18, 30, 0, TimeSpan.Zero));
        var novemberSecond = await NextAsync(session.TenantId, "GR", new DateTimeOffset(2026, 11, 2, 1, 0, 0, TimeSpan.Zero));

        Assert.Equal("GR-2610-0001", october);
        Assert.Equal("GR-2611-0001", november);
        Assert.Equal("GR-2611-0002", novemberSecond);
    }

    [Fact]
    public async Task Rolled_back_transaction_does_not_consume_a_number()
    {
        var session = await fixture.CreateTenantAsync();
        var date = new DateTimeOffset(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(session.TenantId, session.UserId);
            var db = scope.ServiceProvider.GetRequiredService<DbSession>();
            var generator = scope.ServiceProvider.GetRequiredService<IDocumentNumberGenerator>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.ExecuteInTransactionAsync(async ct =>
            {
                Assert.Equal("PR-2610-0001", await generator.NextAsync("PR", date, ct));
                throw new InvalidOperationException("boom");
            }));
        }

        Assert.Equal("PR-2610-0001", await NextAsync(session.TenantId, "PR", date));
    }

    [Fact]
    public async Task Tenant_can_change_the_format()
    {
        var session = await fixture.CreateTenantAsync();

        var saved = await session.Client.PutAsJsonAsync("/api/v1/document-number-formats/PO", new { prefix = "PUR", period = "Year", digits = 6, separator = "/" });
        await saved.ShouldBeAsync(HttpStatusCode.OK);

        var number = await NextAsync(session.TenantId, "PO", new DateTimeOffset(2027, 1, 5, 3, 0, 0, TimeSpan.Zero));
        Assert.Equal("PUR/27/000001", number);
    }

    private async Task<string> NextAsync(Guid tenantId, string documentType, DateTimeOffset date)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId, null);
        return await scope.ServiceProvider.GetRequiredService<IDocumentNumberGenerator>().NextAsync(documentType, date);
    }
}
