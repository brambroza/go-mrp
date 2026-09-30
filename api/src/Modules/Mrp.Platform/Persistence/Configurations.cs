using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mrp.Platform.Domain;

namespace Mrp.Platform.Persistence;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.Property(t => t.Slug).HasMaxLength(40);
        builder.Property(t => t.TimeZone).HasMaxLength(64);
        builder.Property(t => t.DefaultLanguage).HasMaxLength(5);
        builder.HasIndex(t => t.Slug).IsUnique();
        builder.Ignore(t => t.CanSignIn);
    }
}

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("users");
        builder.Property(u => u.ConcurrencyStamp).IsConcurrencyToken();
        builder.Property(u => u.PasswordHash).HasMaxLength(512);
        builder.Property(u => u.Language).HasMaxLength(5);
        builder.HasIndex(u => new { u.TenantId, u.NormalizedUserName }).IsUnique();
        builder.HasIndex(u => new { u.TenantId, u.NormalizedEmail }).IsUnique();
        builder.HasMany<AppUserClaim>().WithOne().HasForeignKey(c => c.UserId).IsRequired();
        builder.HasMany<AppUserLogin>().WithOne().HasForeignKey(l => l.UserId).IsRequired();
        builder.HasMany<AppUserToken>().WithOne().HasForeignKey(t => t.UserId).IsRequired();
        builder.HasMany<AppUserRole>().WithOne().HasForeignKey(r => r.UserId).IsRequired();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AppRoleConfiguration : IEntityTypeConfiguration<AppRole>
{
    public void Configure(EntityTypeBuilder<AppRole> builder)
    {
        builder.ToTable("roles");
        builder.Property(r => r.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasIndex(r => new { r.TenantId, r.NormalizedName }).IsUnique();
        builder.HasMany<AppUserRole>().WithOne().HasForeignKey(r => r.RoleId).IsRequired();
        builder.HasMany<AppRoleClaim>().WithOne().HasForeignKey(c => c.RoleId).IsRequired();
    }
}

internal sealed class AppUserRoleConfiguration : IEntityTypeConfiguration<AppUserRole>
{
    public void Configure(EntityTypeBuilder<AppUserRole> builder)
    {
        builder.ToTable("user_roles");
        builder.HasKey(r => new { r.UserId, r.RoleId });
    }
}

internal sealed class AppUserClaimConfiguration : IEntityTypeConfiguration<AppUserClaim>
{
    public void Configure(EntityTypeBuilder<AppUserClaim> builder)
    {
        builder.ToTable("user_claims");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.ClaimValue).HasMaxLength(1024);
    }
}

internal sealed class AppUserLoginConfiguration : IEntityTypeConfiguration<AppUserLogin>
{
    public void Configure(EntityTypeBuilder<AppUserLogin> builder)
    {
        builder.ToTable("user_logins");
        builder.HasKey(l => new { l.TenantId, l.LoginProvider, l.ProviderKey });
    }
}

internal sealed class AppUserTokenConfiguration : IEntityTypeConfiguration<AppUserToken>
{
    public void Configure(EntityTypeBuilder<AppUserToken> builder)
    {
        builder.ToTable("user_tokens");
        builder.HasKey(t => new { t.UserId, t.LoginProvider, t.Name });
        builder.Property(t => t.Value).HasMaxLength(2048);
    }
}

internal sealed class AppRoleClaimConfiguration : IEntityTypeConfiguration<AppRoleClaim>
{
    public void Configure(EntityTypeBuilder<AppRoleClaim> builder)
    {
        builder.ToTable("role_claims");
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => new { c.RoleId, c.ClaimType, c.ClaimValue }).IsUnique();
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.Property(t => t.TokenHash).HasMaxLength(64);
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => new { t.UserId, t.RevokedAt });
        builder.HasOne<AppUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TenantSettingConfiguration : IEntityTypeConfiguration<TenantSetting>
{
    public void Configure(EntityTypeBuilder<TenantSetting> builder)
    {
        builder.ToTable("tenant_settings");
        builder.Property(s => s.Key).HasMaxLength(100);
        builder.Property(s => s.Value).HasColumnType("jsonb");
        builder.HasIndex(s => new { s.TenantId, s.Key }).IsUnique();
    }
}

internal sealed class DocumentNumberFormatConfiguration : IEntityTypeConfiguration<DocumentNumberFormat>
{
    public void Configure(EntityTypeBuilder<DocumentNumberFormat> builder)
    {
        builder.ToTable("document_number_formats");
        builder.Property(f => f.DocumentType).HasMaxLength(32);
        builder.Property(f => f.Prefix).HasMaxLength(10);
        builder.Property(f => f.Separator).HasMaxLength(1);
        builder.HasIndex(f => new { f.TenantId, f.DocumentType }).IsUnique();
    }
}

internal sealed class DocumentSequenceConfiguration : IEntityTypeConfiguration<DocumentSequence>
{
    public void Configure(EntityTypeBuilder<DocumentSequence> builder)
    {
        builder.ToTable("document_sequences");
        builder.HasKey(s => new { s.TenantId, s.DocumentType, s.PeriodKey });
        builder.Property(s => s.DocumentType).HasMaxLength(32);
        builder.Property(s => s.PeriodKey).HasMaxLength(8);
    }
}

internal sealed class ApprovalRouteConfiguration : IEntityTypeConfiguration<ApprovalRoute>
{
    public void Configure(EntityTypeBuilder<ApprovalRoute> builder)
    {
        builder.ToTable("approval_routes");
        builder.Property(r => r.DocumentType).HasMaxLength(32);
        builder.HasIndex(r => new { r.TenantId, r.DocumentType }).IsUnique();
        builder.HasMany(r => r.Steps).WithOne().HasForeignKey(s => s.RouteId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Steps).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }
}

internal sealed class ApprovalRouteStepConfiguration : IEntityTypeConfiguration<ApprovalRouteStep>
{
    public void Configure(EntityTypeBuilder<ApprovalRouteStep> builder)
    {
        builder.ToTable("approval_route_steps");
        builder.Property(s => s.MinAmount).HasPrecision(18, 4);
        builder.HasIndex(s => new { s.RouteId, s.StepNo }).IsUnique();
        builder.HasOne<AppRole>().WithMany().HasForeignKey(s => s.RoleId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ApprovalRequest> builder)
    {
        builder.ToTable("approval_requests");
        builder.Property(r => r.DocumentType).HasMaxLength(32);
        builder.Property(r => r.DocumentNo).HasMaxLength(40);
        builder.Property(r => r.Title).HasMaxLength(300);
        builder.Property(r => r.Amount).HasPrecision(18, 4);
        builder.Property(r => r.Version).IsRowVersion();
        builder.Ignore(r => r.CurrentStep);
        builder.HasIndex(r => new { r.TenantId, r.Status, r.CurrentRoleId });
        builder.HasIndex(r => new { r.TenantId, r.DocumentType, r.DocumentId })
            .IsUnique()
            .HasFilter("status = 'Pending'");
        builder.HasMany(r => r.Steps).WithOne().HasForeignKey(s => s.RequestId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(r => r.Actions).WithOne().HasForeignKey(a => a.RequestId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Steps).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        builder.Navigation(r => r.Actions).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }
}

internal sealed class ApprovalRequestStepConfiguration : IEntityTypeConfiguration<ApprovalRequestStep>
{
    public void Configure(EntityTypeBuilder<ApprovalRequestStep> builder)
    {
        builder.ToTable("approval_request_steps");
        builder.HasIndex(s => new { s.RequestId, s.StepNo }).IsUnique();
    }
}

internal sealed class ApprovalActionConfiguration : IEntityTypeConfiguration<ApprovalAction>
{
    public void Configure(EntityTypeBuilder<ApprovalAction> builder)
    {
        builder.ToTable("approval_actions");
        builder.Property(a => a.Comment).HasMaxLength(1000);
        builder.HasIndex(a => a.RequestId);
    }
}
