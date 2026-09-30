namespace Mrp.SharedKernel.Domain;

/// <summary>Catalogue of permission codes. Codes are fixed in code; tenants attach them to roles.</summary>
public static class Permissions
{
    /// <summary>Manage users.</summary>
    public const string UsersManage = "platform.users.manage";

    /// <summary>Manage roles and their permissions.</summary>
    public const string RolesManage = "platform.roles.manage";

    /// <summary>Change tenant settings and document numbering.</summary>
    public const string SettingsManage = "platform.settings.manage";

    /// <summary>Configure approval routes.</summary>
    public const string ApprovalsConfigure = "platform.approvals.configure";

    /// <summary>Read master data.</summary>
    public const string MastersRead = "masters.read";

    /// <summary>Create and edit master data.</summary>
    public const string MastersManage = "masters.manage";

    /// <summary>Read stock, lots and movements.</summary>
    public const string InventoryRead = "inventory.read";

    /// <summary>Post goods receipts.</summary>
    public const string InventoryReceive = "inventory.receive";

    /// <summary>Post goods issues.</summary>
    public const string InventoryIssue = "inventory.issue";

    /// <summary>Post transfers between warehouses/locations.</summary>
    public const string InventoryTransfer = "inventory.transfer";

    /// <summary>Post stock adjustments and counts.</summary>
    public const string InventoryAdjust = "inventory.adjust";

    /// <summary>Record QC results and release/hold lots.</summary>
    public const string InventoryQc = "inventory.qc";

    /// <summary>Close and reopen stock periods.</summary>
    public const string InventoryClosePeriod = "inventory.period.close";

    /// <summary>Read purchase requests and orders.</summary>
    public const string PurchasingRead = "purchasing.read";

    /// <summary>Create and edit purchase requests.</summary>
    public const string PurchaseRequestManage = "purchasing.pr.manage";

    /// <summary>Create and edit purchase orders.</summary>
    public const string PurchaseOrderManage = "purchasing.po.manage";

    /// <summary>Read BOMs, MRP results and work orders.</summary>
    public const string ProductionRead = "production.read";

    /// <summary>Create and edit BOMs.</summary>
    public const string BomManage = "production.bom.manage";

    /// <summary>Run MRP and create proposals.</summary>
    public const string MrpRun = "production.mrp.run";

    /// <summary>Create and edit work orders.</summary>
    public const string WorkOrderManage = "production.workorder.manage";

    /// <summary>All permission codes grouped by module, for the role editor.</summary>
    public static IReadOnlyDictionary<string, string[]> ByModule { get; } = new Dictionary<string, string[]>
    {
        ["platform"] = [UsersManage, RolesManage, SettingsManage, ApprovalsConfigure],
        ["masters"] = [MastersRead, MastersManage],
        ["inventory"] = [InventoryRead, InventoryReceive, InventoryIssue, InventoryTransfer, InventoryAdjust, InventoryQc, InventoryClosePeriod],
        ["purchasing"] = [PurchasingRead, PurchaseRequestManage, PurchaseOrderManage],
        ["production"] = [ProductionRead, BomManage, MrpRun, WorkOrderManage],
    };

    /// <summary>Flat set of valid codes.</summary>
    public static IReadOnlySet<string> All { get; } = ByModule.Values.SelectMany(v => v).ToHashSet(StringComparer.Ordinal);
}
