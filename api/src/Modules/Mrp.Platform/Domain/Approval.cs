using Mrp.SharedKernel.Domain;

namespace Mrp.Platform.Domain;

/// <summary>State of an approval request.</summary>
public enum ApprovalStatus
{
    /// <summary>Waiting for the current step.</summary>
    Pending,

    /// <summary>Every step approved.</summary>
    Approved,

    /// <summary>Rejected at a step.</summary>
    Rejected,

    /// <summary>Withdrawn by the requester.</summary>
    Withdrawn,
}

/// <summary>Kind of action recorded on a request.</summary>
public enum ApprovalActionType
{
    /// <summary>Step approved.</summary>
    Approve,

    /// <summary>Request rejected.</summary>
    Reject,

    /// <summary>Request withdrawn.</summary>
    Withdraw,
}

/// <summary>Approval steps a tenant configured for one document type.</summary>
public sealed class ApprovalRoute : Entity
{
    private readonly List<ApprovalRouteStep> _steps = [];

    private ApprovalRoute()
    {
    }

    /// <summary>Creates an active route without steps.</summary>
    public ApprovalRoute(string documentType, string name)
    {
        DocumentType = documentType;
        Name = name;
    }

    /// <summary>Document type key.</summary>
    public string DocumentType { get; private set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Inactive routes are ignored (documents auto-approve).</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Steps ordered by <see cref="ApprovalRouteStep.StepNo"/>.</summary>
    public IReadOnlyList<ApprovalRouteStep> Steps => _steps;

    /// <summary>Replaces name, active flag and all steps; steps are renumbered 1..n in the given order.</summary>
    public void Replace(string name, bool isActive, IEnumerable<ApprovalStepDefinition> steps)
    {
        Name = name;
        IsActive = isActive;
        _steps.Clear();
        var number = 0;
        foreach (var step in steps)
        {
            if (step.MinAmount is < 0)
            {
                throw new DomainException("platform.approval.invalid_min_amount", "Minimum amount must not be negative.", 400);
            }

            _steps.Add(new ApprovalRouteStep(++number, step.Name, step.RoleId, step.MinAmount, step.RequireTwoFactor));
        }
    }

    /// <summary>Steps that apply to a document of the given amount, in order.</summary>
    public IReadOnlyList<ApprovalRouteStep> ApplicableSteps(decimal? amount) =>
        _steps.OrderBy(s => s.StepNo)
            .Where(s => s.MinAmount is null || (amount ?? 0m) >= s.MinAmount)
            .ToList();
}

/// <summary>Input for one route step.</summary>
/// <param name="Name">Step label, e.g. "หัวหน้า".</param>
/// <param name="RoleId">Role whose members may act on the step.</param>
/// <param name="MinAmount">Step applies only when the document amount is at least this value.</param>
/// <param name="RequireTwoFactor">Approver must be signed in with 2FA.</param>
public sealed record ApprovalStepDefinition(string Name, Guid RoleId, decimal? MinAmount, bool RequireTwoFactor);

/// <summary>One step of a route.</summary>
public sealed class ApprovalRouteStep : ITenantOwned
{
    private ApprovalRouteStep()
    {
    }

    internal ApprovalRouteStep(int stepNo, string name, Guid roleId, decimal? minAmount, bool requireTwoFactor)
    {
        StepNo = stepNo;
        Name = name;
        RoleId = roleId;
        MinAmount = minAmount;
        RequireTwoFactor = requireTwoFactor;
    }

    /// <summary>Primary key.</summary>
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Owning route.</summary>
    public Guid RouteId { get; private set; }

    /// <summary>Order within the route, starting at 1.</summary>
    public int StepNo { get; private set; }

    /// <summary>Step label.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Role whose members may act.</summary>
    public Guid RoleId { get; private set; }

    /// <summary>Minimum document amount for the step to apply; null = always.</summary>
    public decimal? MinAmount { get; private set; }

    /// <summary>Approver must have passed 2FA.</summary>
    public bool RequireTwoFactor { get; private set; }
}

/// <summary>A document waiting for (or finished with) approval. Steps are snapshotted at submit time.</summary>
public sealed class ApprovalRequest : Entity
{
    private readonly List<ApprovalRequestStep> _steps = [];
    private readonly List<ApprovalAction> _actions = [];

    private ApprovalRequest()
    {
    }

    /// <summary>Creates a pending request from the applicable route steps.</summary>
    public ApprovalRequest(ApprovalDocument document, Guid requestedBy, IReadOnlyList<ApprovalRouteStep> steps)
    {
        if (steps.Count == 0)
        {
            throw new ArgumentException("A request needs at least one step.", nameof(steps));
        }

        DocumentType = document.DocumentType;
        DocumentId = document.DocumentId;
        DocumentNo = document.DocumentNo;
        Title = document.Title;
        Amount = document.Amount;
        RequestedBy = requestedBy;
        RequestedAt = DateTimeOffset.UtcNow;
        var number = 0;
        foreach (var step in steps)
        {
            _steps.Add(new ApprovalRequestStep(++number, step.Name, step.RoleId, step.RequireTwoFactor));
        }

        TotalSteps = number;
        CurrentStepNo = 1;
        CurrentRoleId = _steps[0].RoleId;
    }

    /// <summary>Document type key.</summary>
    public string DocumentType { get; private set; } = string.Empty;

    /// <summary>Document id in the owning module.</summary>
    public Guid DocumentId { get; private set; }

    /// <summary>Document number.</summary>
    public string DocumentNo { get; private set; } = string.Empty;

    /// <summary>Short description for the inbox.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>Document amount, if any.</summary>
    public decimal? Amount { get; private set; }

    /// <summary>User that submitted the document.</summary>
    public Guid RequestedBy { get; private set; }

    /// <summary>Submit time.</summary>
    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>Current state.</summary>
    public ApprovalStatus Status { get; private set; } = ApprovalStatus.Pending;

    /// <summary>Step waiting for action (1-based).</summary>
    public int CurrentStepNo { get; private set; }

    /// <summary>Role of the step waiting for action; denormalized for inbox queries.</summary>
    public Guid? CurrentRoleId { get; private set; }

    /// <summary>Number of steps.</summary>
    public int TotalSteps { get; private set; }

    /// <summary>Completion time.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Optimistic concurrency token so two approvers cannot act on the same step.</summary>
    public uint Version { get; private set; }

    /// <summary>Snapshotted steps.</summary>
    public IReadOnlyList<ApprovalRequestStep> Steps => _steps;

    /// <summary>Actions taken so far.</summary>
    public IReadOnlyList<ApprovalAction> Actions => _actions;

    /// <summary>Step waiting for action.</summary>
    public ApprovalRequestStep CurrentStep => _steps.Single(s => s.StepNo == CurrentStepNo);

    /// <summary>Approves the current step. Returns true when the whole request became approved.</summary>
    /// <param name="approver">Acting user and what is known about their session.</param>
    /// <param name="comment">Optional remark.</param>
    /// <param name="allowSelfApprove">Tenant setting <c>approval.allowSelfApprove</c>.</param>
    public bool Approve(ApproverContext approver, string? comment, bool allowSelfApprove)
    {
        EnsureCanAct(approver, allowSelfApprove);
        _actions.Add(new ApprovalAction(CurrentStepNo, ApprovalActionType.Approve, approver.UserId, comment));
        if (CurrentStepNo < TotalSteps)
        {
            CurrentStepNo++;
            CurrentRoleId = CurrentStep.RoleId;
            return false;
        }

        Complete(ApprovalStatus.Approved);
        return true;
    }

    /// <summary>Rejects the request at the current step. A comment is mandatory.</summary>
    public void Reject(ApproverContext approver, string? comment, bool allowSelfApprove)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            throw new DomainException("platform.approval.comment_required", "A reason is required to reject.", 400);
        }

        EnsureCanAct(approver, allowSelfApprove);
        _actions.Add(new ApprovalAction(CurrentStepNo, ApprovalActionType.Reject, approver.UserId, comment));
        Complete(ApprovalStatus.Rejected);
    }

    /// <summary>Withdraws the request; only the requester may do so.</summary>
    public void Withdraw(Guid userId, string? comment)
    {
        EnsurePending();
        if (userId != RequestedBy)
        {
            throw DomainException.Forbidden("platform.approval.not_requester", "Only the requester can withdraw.");
        }

        _actions.Add(new ApprovalAction(CurrentStepNo, ApprovalActionType.Withdraw, userId, comment));
        Complete(ApprovalStatus.Withdrawn);
    }

    private void EnsureCanAct(ApproverContext approver, bool allowSelfApprove)
    {
        EnsurePending();
        var step = CurrentStep;
        if (!approver.RoleIds.Contains(step.RoleId))
        {
            throw DomainException.Forbidden("platform.approval.not_approver", "You are not an approver of the current step.");
        }

        if (!allowSelfApprove && approver.UserId == RequestedBy)
        {
            throw DomainException.Forbidden("platform.approval.self_approve", "You cannot approve your own document.");
        }

        if (step.RequireTwoFactor && !approver.PassedTwoFactor)
        {
            throw DomainException.Forbidden("platform.approval.two_factor_required", "This step requires two-factor authentication.");
        }
    }

    private void EnsurePending()
    {
        if (Status != ApprovalStatus.Pending)
        {
            throw DomainException.Conflict("platform.approval.not_pending", $"The request is already {Status}.");
        }
    }

    private void Complete(ApprovalStatus status)
    {
        Status = status;
        CurrentRoleId = null;
        CompletedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>Who is acting on an approval step.</summary>
/// <param name="UserId">Acting user.</param>
/// <param name="RoleIds">Roles the user holds.</param>
/// <param name="PassedTwoFactor">Whether the session passed 2FA.</param>
public sealed record ApproverContext(Guid UserId, IReadOnlyCollection<Guid> RoleIds, bool PassedTwoFactor);

/// <summary>Step of a request, copied from the route at submit time.</summary>
public sealed class ApprovalRequestStep : ITenantOwned
{
    private ApprovalRequestStep()
    {
    }

    internal ApprovalRequestStep(int stepNo, string name, Guid roleId, bool requireTwoFactor)
    {
        StepNo = stepNo;
        Name = name;
        RoleId = roleId;
        RequireTwoFactor = requireTwoFactor;
    }

    /// <summary>Primary key.</summary>
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Owning request.</summary>
    public Guid RequestId { get; private set; }

    /// <summary>Order, starting at 1.</summary>
    public int StepNo { get; private set; }

    /// <summary>Step label.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Role whose members may act.</summary>
    public Guid RoleId { get; private set; }

    /// <summary>Approver must have passed 2FA.</summary>
    public bool RequireTwoFactor { get; private set; }
}

/// <summary>Audit record of an approval action. Never updated.</summary>
public sealed class ApprovalAction : ITenantOwned
{
    private ApprovalAction()
    {
    }

    internal ApprovalAction(int stepNo, ApprovalActionType action, Guid actedBy, string? comment)
    {
        StepNo = stepNo;
        Action = action;
        ActedBy = actedBy;
        ActedAt = DateTimeOffset.UtcNow;
        Comment = comment is { Length: > 1000 } ? comment[..1000] : comment;
    }

    /// <summary>Primary key.</summary>
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Owning request.</summary>
    public Guid RequestId { get; private set; }

    /// <summary>Step the action was taken on.</summary>
    public int StepNo { get; private set; }

    /// <summary>Kind of action.</summary>
    public ApprovalActionType Action { get; private set; }

    /// <summary>Acting user.</summary>
    public Guid ActedBy { get; private set; }

    /// <summary>Action time.</summary>
    public DateTimeOffset ActedAt { get; private set; }

    /// <summary>Remark.</summary>
    public string? Comment { get; private set; }
}
