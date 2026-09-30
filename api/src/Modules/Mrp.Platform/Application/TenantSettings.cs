using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mrp.Platform.Domain;
using Mrp.Platform.Persistence;
using Mrp.SharedKernel.Domain;
using Mrp.SharedKernel.Tenancy;

namespace Mrp.Platform.Application;

/// <summary>Reads and writes settings of the current tenant; values are cached for the scope.</summary>
public sealed class TenantSettingsService(PlatformDbContext db, ITenantContext tenant) : ITenantSettings
{
    private Dictionary<string, string>? _cache;
    private TimeZoneInfo? _timeZone;

    /// <inheritdoc />
    public async Task<T> GetAsync<T>(string key, T defaultValue, CancellationToken cancellationToken = default)
    {
        var values = await LoadAsync(cancellationToken);
        if (!values.TryGetValue(key, out var json))
        {
            return defaultValue;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web) ?? defaultValue;
        }
        catch (JsonException)
        {
            return defaultValue;
        }
    }

    /// <inheritdoc />
    public async Task<TimeZoneInfo> GetTimeZoneAsync(CancellationToken cancellationToken = default)
    {
        if (_timeZone is not null)
        {
            return _timeZone;
        }

        var tenantId = tenant.RequireTenantId();
        var id = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.TimeZone).FirstOrDefaultAsync(cancellationToken);
        _timeZone = TimeZoneInfo.TryFindSystemTimeZoneById(id ?? "Asia/Bangkok", out var zone)
            ? zone
            : TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok");
        return _timeZone;
    }

    /// <summary>All stored settings of the tenant.</summary>
    public async Task<IReadOnlyList<SettingDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var values = await LoadAsync(cancellationToken);
        return values.OrderBy(v => v.Key, StringComparer.Ordinal)
            .Select(v => new SettingDto(v.Key, JsonDocument.Parse(v.Value).RootElement.Clone()))
            .ToList();
    }

    /// <summary>Stores settings after validating key and value type.</summary>
    public async Task SaveAsync(IReadOnlyList<SettingDto> settings, CancellationToken cancellationToken = default)
    {
        foreach (var setting in settings)
        {
            if (!SettingKeys.Known.TryGetValue(setting.Key, out var kinds) || !kinds.Contains(setting.Value.ValueKind))
            {
                throw new DomainException("platform.settings.invalid", $"Setting '{setting.Key}' is unknown or has the wrong type.", 400);
            }

            Validate(setting);
            var json = setting.Value.GetRawText();
            var existing = await db.Settings.FirstOrDefaultAsync(s => s.Key == setting.Key, cancellationToken);
            if (existing is null)
            {
                db.Settings.Add(new TenantSetting(setting.Key, json));
            }
            else
            {
                existing.SetValue(json);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        _cache = null;
    }

    private static void Validate(SettingDto setting)
    {
        var valid = setting.Key switch
        {
            SettingKeys.IssueStrategy => setting.Value.GetString() is "Fifo" or "Fefo",
            SettingKeys.OverReceivePercent or SettingKeys.VatPercent or SettingKeys.OverIssuePercent =>
                setting.Value.TryGetDecimal(out var number) && number is >= 0 and <= 100,
            _ => true,
        };
        if (!valid)
        {
            throw new DomainException("platform.settings.invalid", $"Value of '{setting.Key}' is out of range.", 400);
        }
    }

    private async Task<Dictionary<string, string>> LoadAsync(CancellationToken cancellationToken) =>
        _cache ??= await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, StringComparer.Ordinal, cancellationToken);
}
