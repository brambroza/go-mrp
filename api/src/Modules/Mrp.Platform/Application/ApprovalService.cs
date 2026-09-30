using Microsoft.EntityFrameworkCore;
using Mrp.Platform.Domain;
using Mrp.Platform.Persistence;
using Mrp.SharedKernel.Domain;
using Mrp.SharedKernel.Persistence;
using Mrp.SharedKernel.Tenancy;
using Mrp.SharedKernel.Web;

namespace Mrp.Platform.Application;

/// <summary>Approval engine: routes documents through the tenant's configured steps and serves the inbox.</summary>
public sealed class ApprovalService(
    PlatformDbContext db,
    DbSession session,
    ITenantContext tenant,
    ITenantSettings settings,
    IEnumerable<IApprovalSubscriber> subscribers) : IApprovalService
{
    /// <inheritdoc />
    public async Task<ApprovalSubmission> SubmitAsync(ApprovalDocument document, CancellationToken cancellationToken = default)
    {
        var route = await db.ApprovalRoutes.AsNoTracking()
            .FirstOrDefaultAsync(r => r.DocumentType == document.DocumentType && r.IsActive, cancellationToken);
        var steps = route?.ApplicableSteps(document.Amount) ?? [];
        if (steps.Count == 0)
        {
            return new ApprovalSubmission(null, AutoApproved: true);
        }

        var request = new ApprovalRequest(document, tenant.RequireUserId(), steps);
        db.ApprovalRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);
        return new ApprovalSubmission(request.Id, AutoApproved: false);
    }

    /// <inheritdoc />
    public async Task WithdrawAsync(string documentType, Guid documentId, CancellationToken cancellationToken = default)
    {
        var request = await db.ApprovalRequests.FirstOrDefaultAsync(
            r => r.DocumentType == documentType && r.DocumentId == documentId && r.Status == ApprovalStatus.Pending,
            cancellationToken);
        if (request is null)
        {
            return;
        }

        request.Withdraw(tenant.RequireUserId(), null);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Approves the current step; on the last step the owning module is notified in the same transaction.</summary>
    public Task<ApprovalRequestDto> ApproveAsync(Guid requestId, string? comment, bool passedTwoFactor, CancellationToken cancellationToken) =>
        ActAsync(requestId, passedTwoFactor, (request, approver, allowSelf) =>
            request.Approve(approver, comment, allowSelf) ? ApprovalOutcome.Approved : null, comment, cancellationToken);

    /// <summary>Rejects the request and notifies the owning module.</summary>
    public Task<ApprovalRequestDto> RejectAsync(Guid requestId, string? comment, bool passedTwoFactor, CancellationToken cancellationToken) =>
        ActAsync(requestId, passedTwoFactor, (request, approver, allowSelf) =>
        {
            request.Reject(approver, comment, allowSelf);
            return ApprovalOutcome.Rejected;
        }, comment, cancellationToken);

    /// <summary>Withdraws the request (requester only) and notifies the owning module.</summary>
    public Task<ApprovalRequestDto> WithdrawAsync(Guid requestId, string? comment, CancellationToken cancellationToken) =>
        ActAsync(requestId, passedTwoFactor: false, (request, approver, _) =>
        {
            request.Withdraw(approver.UserId, comment);
            return ApprovalOutcome.Withdrawn;
        }, comment, cancellationToken);

    /// <summary>Pending requests the current user can act on.</summary>
    public async Task<PagedResult<ApprovalRequestDto>> InboxAsync(int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var userId = tenant.RequireUserId();
        var roleIds = await RoleIdsAsync(userId, cancellationToken);
        var allowSelf = await settings.GetAsync(SettingKeys.AllowSelfApprove, false, cancellationToken);
        var query = db.ApprovalRequests.AsNoTracking()
            .Where(r => r.Status == ApprovalStatus.Pending && r.CurrentRoleId != null && roleIds.Contains(r.CurrentRoleId.Value))
            .Where(r => allowSelf || r.RequestedBy != userId)
            .OrderBy(r => r.RequestedAt);
        return await ToDtoPageAsync(query, page, pageSize, canAct: _ => true, cancellationToken);
    }

    /// <summary>Requests submitted by the current user, newest first.</summary>
    public async Task<PagedResult<ApprovalRequestDto>> MineAsync(int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var userId = tenant.RequireUserId();
        var query = db.ApprovalRequests.AsNoTracking()
            .Where(r => r.RequestedBy == userId)
            .OrderByDescending(r => r.RequestedAt);
        return await ToDtoPageAsync(query, page, pageSize, canAct: _ => false, cancellationToken);
    }

    /// <summary>One request with its history.</summary>
    public async Task<ApprovalRequestDto> GetAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var request = await db.ApprovalRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken)
            ?? throw DomainException.NotFound("Approval request", requestId);
        var userId = tenant.RequireUserId();
        var roleIds = await RoleIdsAsync(userId, cancellationToken);
        var allowSelf = await settings.GetAsync(SettingKeys.AllowSelfApprove, false, cancellationToken);
        var names = await UserNamesAsync([request], cancellationToken);
        return ToDto(request, names, CanAct(request, userId, roleIds, allowSelf));
    }

    /// <summary>History of a document's approval requests, newest first.</summary>
    public async Task<IReadOnlyList<ApprovalRequestDto>> ForDocumentAsync(string documentType, Guid documentId, CancellationToken cancellationToken)
    {
        var requests = await db.ApprovalRequests.AsNoTracking()
            .Where(r => r.DocumentType == documentType && r.DocumentId == documentId)
            .OrderByDescending(r => r.RequestedAt)
            .ToListAsync(cancellationToken);
        var userId = tenant.RequireUserId();
        var roleIds = await RoleIdsAsync(userId, cancellationToken);
        var allowSelf = await settings.GetAsync(SettingKeys.AllowSelfApprove, false, cancellationToken);
        var names = await UserNamesAsync(requests, cancellationToken);
        return requests.Select(r => ToDto(r, names, CanAct(r, userId, roleIds, allowSelf))).ToList();
    }

    /// <summary>Routes of all document types that have one.</summary>
    public async Task<IReadOnlyList<ApprovalRouteDto>> ListRoutesAsync(CancellationToken cancellationToken)
    {
        var routes = await db.ApprovalRoutes.AsNoTracking().OrderBy(r => r.DocumentType).ToListAsync(cancellationToken);
        return routes.Select(ToDto).ToList();
    }

    /// <summary>Creates or replaces the route of a document type.</summary>
    public async Task<ApprovalRouteDto> SaveRouteAsync(string documentType, SaveApprovalRouteRequest request, CancellationToken cancellationToken)
    {
        if (!DocumentTypes.All.Contains(documentType))
        {
            throw new DomainException("platform.approval.unknown_document_type", $"Document type '{documentType}' is not known.", 400);
        }

        var roleIds = request.Steps.Select(s => s.RoleId).Distinct().ToList();
        var known = await db.Roles.CountAsync(r => roleIds.Contains(r.Id), cancellationToken);
        if (known != roleIds.Count)
        {
            throw new DomainException("platform.approval.unknown_role", "A step refers to a role that does not exist.", 400);
        }

        return await session.ExecuteInTransactionAsync(
            async ct =>
            {
                var route = await db.ApprovalRoutes.FirstOrDefaultAsync(r => r.DocumentType == documentType, ct);
                if (route is null)
                {
                    route = new ApprovalRoute(documentType, request.Name);
                    db.ApprovalRoutes.Add(route);
                }

                route.Replace(
                    request.Name,
                    request.IsActive,
                    request.Steps.OrderBy(s => s.StepNo).Select(s => new ApprovalStepDefinition(s.Name, s.RoleId, s.MinAmount, s.RequireTwoFactor)));
                await db.SaveChangesAsync(ct);
                return ToDto(route);
            },
            cancellationToken);
    }

    private static bool CanAct(ApprovalRequest request, Guid userId, IReadOnlyCollection<Guid> roleIds, bool allowSelf) =>
        request.Status == ApprovalStatus.Pending
        && request.CurrentRoleId is { } roleId
        && roleIds.Contains(roleId)
        && (allowSelf || request.RequestedBy != userId);

    private static ApprovalRouteDto ToDto(ApprovalRoute route) =>
        new(route.Id, route.DocumentType, route.Name, route.IsActive,
            route.Steps.OrderBy(s => s.StepNo)
                .Select(s => new ApprovalRouteStepDto(s.StepNo, s.Name, s.RoleId, s.MinAmount, s.RequireTwoFactor))
                .ToList());

    private static ApprovalRequestDto ToDto(ApprovalRequest request, IReadOnlyDictionary<Guid, string> names, bool canAct) =>
        new(request.Id, request.DocumentType, request.DocumentId, request.DocumentNo, request.Title, request.Amount,
            request.RequestedBy, names.GetValueOrDefault(request.RequestedBy, string.Empty), request.RequestedAt,
            request.Status, request.CurrentStepNo, request.TotalSteps,
            request.Status == ApprovalStatus.Pending ? request.CurrentStep.Name : null,
            canAct,
            request.Actions.OrderBy(a => a.ActedAt)
                .Select(a => new ApprovalActionDto(a.StepNo, a.Action, a.ActedBy, names.GetValueOrDefault(a.ActedBy, string.Empty), a.ActedAt, a.Comment))
                .ToList());

    private async Task<ApprovalRequestDto> ActAsync(
        Guid requestId,
        bool passedTwoFactor,
        Func<ApprovalRequest, ApproverContext, bool, ApprovalOutcome?> act,
        string? comment,
        CancellationToken cancellationToken)
    {
        var userId = tenant.RequireUserId();
        await session.ExecuteInTransactionAsync(
            async ct =>
            {
                var request = await db.ApprovalRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct)
                    ?? throw DomainException.NotFound("Approval request", requestId);
                var roleIds = await RoleIdsAsync(userId, ct);
                var allowSelf = await settings.GetAsync(SettingKeys.AllowSelfApprove, false, ct);
                var outcome = act(request, new ApproverContext(userId, roleIds, passedTwoFactor), allowSelf);
                await db.SaveChangesAsync(ct);

                if (outcome is { } final)
                {
                    foreach (var subscriber in subscribers.Where(s => s.DocumentType == request.DocumentType))
                    {
                        await subscriber.OnCompletedAsync(request.DocumentId, final, userId, comment, ct);
                    }
                }
            },
            cancellationToken);
        return await GetAsync(requestId, cancellationToken);
    }

    private async Task<List<Guid>> RoleIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.UserRoles.Where(r => r.UserId == userId).Select(r => r.RoleId).ToListAsync(cancellationToken);

    private async Task<IReadOnlyDictionary<Guid, string>> UserNamesAsync(IReadOnlyCollection<ApprovalRequest> requests, CancellationToken cancellationToken)
    {
        var ids = requests.Select(r => r.RequestedBy)
            .Concat(requests.SelectMany(r => r.Actions).Select(a => a.ActedBy))
            .Distinct()
            .ToList();
        return await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
    }

    private async Task<PagedResult<ApprovalRequestDto>> ToDtoPageAsync(
        IQueryable<ApprovalRequest> query,
        int? page,
        int? pageSize,
        Func<ApprovalRequest, bool> canAct,
        CancellationToken cancellationToken)
    {
        var result = await query.ToPagedAsync(page, pageSize, cancellationToken);
        var names = await UserNamesAsync(result.Items, cancellationToken);
        return new PagedResult<ApprovalRequestDto>(
            result.Items.Select(r => ToDto(r, names, canAct(r))).ToList(), result.Total, result.Page, result.PageSize);
    }
}
