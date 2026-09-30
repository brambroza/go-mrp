using Mrp.SharedKernel.Domain;

namespace Mrp.Inventory.Domain;

/// <summary>Quality status of a lot.</summary>
public enum LotQcStatus
{
    /// <summary>Waiting for QC; not available.</summary>
    Quarantine,

    /// <summary>Passed; available to issue.</summary>
    Released,

    /// <summary>Failed; may only be returned or adjusted out.</summary>
    Rejected,

    /// <summary>Temporarily blocked after release.</summary>
    OnHold,
}

/// <summary>A lot of one item. Its number is what gets printed as a barcode.</summary>
public sealed class Lot : Entity
{
    private static readonly StateMachine<LotQcStatus, LotQcStatus> Transitions = new StateMachine<LotQcStatus, LotQcStatus>()
        .Permit(LotQcStatus.Quarantine, LotQcStatus.Released, LotQcStatus.Released)
        .Permit(LotQcStatus.Quarantine, LotQcStatus.Rejected, LotQcStatus.Rejected)
        .Permit(LotQcStatus.Released, LotQcStatus.OnHold, LotQcStatus.OnHold)
        .Permit(LotQcStatus.OnHold, LotQcStatus.Released, LotQcStatus.Released)
        .Permit(LotQcStatus.OnHold, LotQcStatus.Rejected, LotQcStatus.Rejected);

    private Lot()
    {
    }

    /// <summary>Creates a lot.</summary>
    public Lot(Guid itemId, string lotNo, LotQcStatus qcStatus, decimal unitCost, DateTimeOffset receivedAt, string? supplierLot, DateOnly? mfgDate, DateOnly? expiryDate, Guid? sourceDocumentId)
    {
        if (string.IsNullOrWhiteSpace(lotNo) || lotNo.Length > 40)
        {
            throw new DomainException("inventory.lot.invalid_number", "Lot number must be 1-40 characters.", 400);
        }

        if (unitCost < 0)
        {
            throw new DomainException("inventory.lot.negative_cost", "Unit cost must not be negative.", 400);
        }

        if (mfgDate is { } mfg && expiryDate is { } expiry && expiry < mfg)
        {
            throw new DomainException("inventory.lot.expiry_before_mfg", "Expiry date must not be before the manufacturing date.", 400);
        }

        ItemId = itemId;
        LotNo = lotNo.Trim().ToUpperInvariant();
        QcStatus = qcStatus;
        UnitCost = unitCost;
        ReceivedAt = receivedAt;
        SupplierLot = string.IsNullOrWhiteSpace(supplierLot) ? null : supplierLot.Trim();
        MfgDate = mfgDate;
        ExpiryDate = expiryDate;
        SourceDocumentId = sourceDocumentId;
    }

    /// <summary>Item of the lot.</summary>
    public Guid ItemId { get; private set; }

    /// <summary>Tenant-unique lot number.</summary>
    public string LotNo { get; private set; } = string.Empty;

    /// <summary>Supplier's own lot/batch number.</summary>
    public string? SupplierLot { get; private set; }

    /// <summary>Manufacturing date.</summary>
    public DateOnly? MfgDate { get; private set; }

    /// <summary>Expiry date; null = does not expire.</summary>
    public DateOnly? ExpiryDate { get; private set; }

    /// <summary>Time the lot entered stock; decides FIFO order.</summary>
    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>Quality status.</summary>
    public LotQcStatus QcStatus { get; private set; }

    /// <summary>Cost per stock unit.</summary>
    public decimal UnitCost { get; private set; }

    /// <summary>Document that created the lot.</summary>
    public Guid? SourceDocumentId { get; private set; }

    /// <summary>Remark of the last QC decision.</summary>
    public string? QcRemark { get; private set; }

    /// <summary>Computes the expiry date from the manufacturing (or receipt) date and the item's shelf life.</summary>
    public static DateOnly? DefaultExpiry(DateOnly? mfgDate, DateOnly receivedOn, int? shelfLifeDays) =>
        shelfLifeDays is { } days ? (mfgDate ?? receivedOn).AddDays(days) : null;

    /// <summary>Records a QC decision.</summary>
    public void SetQcStatus(LotQcStatus target, string? remark, DateOnly? expiryDate)
    {
        if (!Transitions.CanFire(QcStatus, target))
        {
            throw DomainException.Conflict("inventory.lot.invalid_qc_transition", $"A lot that is {QcStatus} cannot become {target}.");
        }

        if (expiryDate is { } expiry)
        {
            if (MfgDate is { } mfg && expiry < mfg)
            {
                throw new DomainException("inventory.lot.expiry_before_mfg", "Expiry date must not be before the manufacturing date.", 400);
            }

            ExpiryDate = expiry;
        }

        QcStatus = target;
        QcRemark = remark is { Length: > 500 } ? remark[..500] : remark;
    }
}
