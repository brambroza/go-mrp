using System.Text.Json;

namespace Mrp.SharedKernel.Domain;

/// <summary>Known setting keys and their defaults.</summary>
public static class SettingKeys
{
    /// <summary>Whether a requester may approve their own document (default false).</summary>
    public const string AllowSelfApprove = "approval.allowSelfApprove";

    /// <summary>Whether stock may go negative (default false).</summary>
    public const string AllowNegativeStock = "inventory.allowNegativeStock";

    /// <summary>Lot picking order for issues: <c>Fifo</c> (receive date) or <c>Fefo</c> (expiry first).</summary>
    public const string IssueStrategy = "inventory.issueStrategy";

    /// <summary>Whether received lots wait for QC before they become available (default false).</summary>
    public const string QcOnReceive = "inventory.qcOnReceive";

    /// <summary>Over-receive tolerance against the PO line in percent (default 0).</summary>
    public const string OverReceivePercent = "purchasing.overReceivePercent";

    /// <summary>Default VAT rate in percent (default 7).</summary>
    public const string VatPercent = "purchasing.vatPercent";

    /// <summary>Over-issue tolerance against the work order requirement in percent (default 0).</summary>
    public const string OverIssuePercent = "production.overIssuePercent";

    /// <summary>Overhead added to the material cost of a manufactured item, in percent of material cost (default 0).</summary>
    public const string OverheadPercent = "costing.overheadPercent";

    /// <summary>Working days of the week for scheduling, ISO numbers 1 (Monday) to 7 (Sunday); default Monday–Friday.</summary>
    public const string WorkDays = "scheduling.workDays";

    /// <summary>Working shifts per day as objects <c>{ "start": "08:00", "end": "17:00" }</c>; default two shifts 08:00–12:00 and 13:00–17:00.</summary>
    public const string Shifts = "scheduling.shifts";

    /// <summary>Keys clients may read and write, with JSON value kind.</summary>
    public static IReadOnlyDictionary<string, JsonValueKind[]> Known { get; } = new Dictionary<string, JsonValueKind[]>
    {
        [AllowSelfApprove] = [JsonValueKind.True, JsonValueKind.False],
        [AllowNegativeStock] = [JsonValueKind.True, JsonValueKind.False],
        [IssueStrategy] = [JsonValueKind.String],
        [QcOnReceive] = [JsonValueKind.True, JsonValueKind.False],
        [OverReceivePercent] = [JsonValueKind.Number],
        [VatPercent] = [JsonValueKind.Number],
        [OverIssuePercent] = [JsonValueKind.Number],
        [OverheadPercent] = [JsonValueKind.Number],
        [WorkDays] = [JsonValueKind.Array],
        [Shifts] = [JsonValueKind.Array],
    };
}
