namespace Mrp.Platform.Domain;

/// <summary>Subscription package of a tenant.</summary>
public enum TenantPlan
{
    /// <summary>Inventory and purchasing.</summary>
    Starter,

    /// <summary>Starter plus production and MRP.</summary>
    Pro,

    /// <summary>Pro plus sales/invoice, lab, API.</summary>
    Enterprise,
}

/// <summary>Lifecycle state of a tenant.</summary>
public enum TenantStatus
{
    /// <summary>Free trial.</summary>
    Trial,

    /// <summary>Paying customer.</summary>
    Active,

    /// <summary>Access blocked (unpaid or requested).</summary>
    Suspended,
}

/// <summary>A customer company. The only table without <c>tenant_id</c>: it is the tenant.</summary>
public sealed class Tenant
{
    private Tenant()
    {
    }

    /// <summary>Creates a tenant in trial state.</summary>
    /// <param name="slug">URL-safe unique code used at login.</param>
    /// <param name="name">Company name.</param>
    /// <param name="plan">Subscription package.</param>
    public Tenant(string slug, string name, TenantPlan plan)
    {
        Slug = NormalizeSlug(slug);
        Name = name.Trim();
        Plan = plan;
        MaxUsers = DefaultMaxUsers(plan);
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Primary key.</summary>
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <summary>Unique login code, lowercase letters, digits and dashes.</summary>
    public string Slug { get; private set; } = string.Empty;

    /// <summary>Company name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Subscription package.</summary>
    public TenantPlan Plan { get; private set; }

    /// <summary>Lifecycle state.</summary>
    public TenantStatus Status { get; private set; } = TenantStatus.Trial;

    /// <summary>IANA time zone used for document periods and display.</summary>
    public string TimeZone { get; private set; } = "Asia/Bangkok";

    /// <summary>Default UI language (<c>th</c> or <c>en</c>).</summary>
    public string DefaultLanguage { get; private set; } = "th";

    /// <summary>Maximum number of active users allowed by the package.</summary>
    public int MaxUsers { get; private set; }

    /// <summary>Creation time in UTC.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Whether users of this tenant may sign in.</summary>
    public bool CanSignIn => Status != TenantStatus.Suspended;

    /// <summary>Default user limit of a package.</summary>
    public static int DefaultMaxUsers(TenantPlan plan) => plan switch
    {
        TenantPlan.Starter => 5,
        TenantPlan.Pro => 15,
        _ => 30,
    };

    /// <summary>Lowercases and validates a slug (3–40 chars, <c>a-z 0-9 -</c>).</summary>
    public static string NormalizeSlug(string slug)
    {
        var value = (slug ?? string.Empty).Trim().ToLowerInvariant();
        var valid = value.Length is >= 3 and <= 40
            && value.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')
            && value[0] != '-' && value[^1] != '-';
        if (!valid)
        {
            throw new SharedKernel.Domain.DomainException(
                "platform.tenant.invalid_slug",
                "Company code must be 3-40 characters of a-z, 0-9 or '-'.",
                400);
        }

        return value;
    }
}
