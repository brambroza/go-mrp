using Mrp.Platform.Domain;
using Mrp.SharedKernel.Domain;

namespace Mrp.Domain.Tests.Platform;

public sealed class ApprovalRequestTests
{
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly Guid Supervisor = Guid.NewGuid();
    private static readonly Guid Manager = Guid.NewGuid();
    private static readonly Guid SupervisorRole = Guid.NewGuid();
    private static readonly Guid ManagerRole = Guid.NewGuid();

    [Fact]
    public void Two_step_route_is_approved_after_both_steps()
    {
        var request = NewRequest(amount: 50_000m);

        var finishedAfterFirst = request.Approve(Approver(Supervisor, SupervisorRole), "ok", allowSelfApprove: false);
        var finishedAfterSecond = request.Approve(Approver(Manager, ManagerRole), null, allowSelfApprove: false);

        Assert.False(finishedAfterFirst);
        Assert.True(finishedAfterSecond);
        Assert.Equal(ApprovalStatus.Approved, request.Status);
        Assert.Equal(2, request.Actions.Count);
        Assert.Null(request.CurrentRoleId);
    }

    [Fact]
    public void Step_with_minimum_amount_is_skipped_for_small_documents()
    {
        var request = NewRequest(amount: 5_000m);

        Assert.Equal(1, request.TotalSteps);
        Assert.True(request.Approve(Approver(Supervisor, SupervisorRole), null, allowSelfApprove: false));
    }

    [Fact]
    public void Step_with_minimum_amount_applies_at_the_threshold()
    {
        var request = NewRequest(amount: 10_000m);

        Assert.Equal(2, request.TotalSteps);
    }

    [Fact]
    public void User_without_the_role_of_the_current_step_cannot_approve()
    {
        var request = NewRequest(amount: 50_000m);

        var error = Assert.Throws<DomainException>(() => request.Approve(Approver(Manager, ManagerRole), null, allowSelfApprove: false));

        Assert.Equal("platform.approval.not_approver", error.Code);
        Assert.Equal(1, request.CurrentStepNo);
    }

    [Fact]
    public void Requester_cannot_approve_own_document_unless_tenant_allows_it()
    {
        var request = NewRequest(amount: 5_000m);

        var error = Assert.Throws<DomainException>(() => request.Approve(Approver(Requester, SupervisorRole), null, allowSelfApprove: false));
        Assert.Equal("platform.approval.self_approve", error.Code);

        Assert.True(request.Approve(Approver(Requester, SupervisorRole), null, allowSelfApprove: true));
    }

    [Fact]
    public void Step_requiring_two_factor_rejects_sessions_without_it()
    {
        var request = NewRequest(amount: 50_000m);
        request.Approve(Approver(Supervisor, SupervisorRole), null, allowSelfApprove: false);

        var error = Assert.Throws<DomainException>(() =>
            request.Approve(new ApproverContext(Manager, [ManagerRole], PassedTwoFactor: false), null, allowSelfApprove: false));

        Assert.Equal("platform.approval.two_factor_required", error.Code);
        Assert.Equal(ApprovalStatus.Pending, request.Status);
    }

    [Fact]
    public void Reject_needs_a_reason_and_ends_the_request()
    {
        var request = NewRequest(amount: 50_000m);

        var missing = Assert.Throws<DomainException>(() => request.Reject(Approver(Supervisor, SupervisorRole), " ", allowSelfApprove: false));
        Assert.Equal("platform.approval.comment_required", missing.Code);

        request.Reject(Approver(Supervisor, SupervisorRole), "ราคาสูงเกิน", allowSelfApprove: false);

        Assert.Equal(ApprovalStatus.Rejected, request.Status);
        var again = Assert.Throws<DomainException>(() => request.Approve(Approver(Supervisor, SupervisorRole), null, allowSelfApprove: false));
        Assert.Equal("platform.approval.not_pending", again.Code);
    }

    [Fact]
    public void Only_the_requester_can_withdraw()
    {
        var request = NewRequest(amount: 50_000m);

        var error = Assert.Throws<DomainException>(() => request.Withdraw(Supervisor, null));
        Assert.Equal("platform.approval.not_requester", error.Code);

        request.Withdraw(Requester, "แก้เอกสาร");
        Assert.Equal(ApprovalStatus.Withdrawn, request.Status);
    }

    private static ApproverContext Approver(Guid userId, Guid roleId) => new(userId, [roleId], PassedTwoFactor: true);

    private static ApprovalRequest NewRequest(decimal amount)
    {
        var route = new ApprovalRoute("PO", "อนุมัติ 2 ขั้น");
        route.Replace("อนุมัติ 2 ขั้น", true,
        [
            new ApprovalStepDefinition("หัวหน้า", SupervisorRole, null, false),
            new ApprovalStepDefinition("ผู้จัดการ", ManagerRole, 10_000m, true),
        ]);
        var document = new ApprovalDocument("PO", Guid.NewGuid(), "PO-2610-0001", "ซื้อวัตถุดิบ", amount);
        return new ApprovalRequest(document, Requester, route.ApplicableSteps(amount));
    }
}

public sealed class DocumentNumberFormatTests
{
    [Theory]
    [InlineData(NumberingPeriod.Month, 4, "-", 1, "PO-2610-0001")]
    [InlineData(NumberingPeriod.Year, 5, "-", 123, "PO-26-00123")]
    [InlineData(NumberingPeriod.None, 6, "", 42, "PO000042")]
    [InlineData(NumberingPeriod.Month, 3, "/", 12345, "PO/2610/12345")]
    public void Formats_number(NumberingPeriod period, int digits, string separator, long running, string expected)
    {
        var format = new DocumentNumberFormat("PO", "po", period, digits, separator);

        Assert.Equal(expected, format.Format(new DateTime(2026, 10, 15), running));
    }

    [Theory]
    [InlineData("", 4, "-")]
    [InlineData("TOO-LONG-PREFIX", 4, "-")]
    [InlineData("PO", 2, "-")]
    [InlineData("PO", 11, "-")]
    [InlineData("PO", 4, "#")]
    public void Rejects_invalid_format(string prefix, int digits, string separator)
    {
        Assert.Throws<DomainException>(() => new DocumentNumberFormat("PO", prefix, NumberingPeriod.Month, digits, separator));
    }
}

public sealed class TenantTests
{
    [Theory]
    [InlineData("  DK-Factory ", "dk-factory")]
    [InlineData("abc", "abc")]
    public void Normalizes_slug(string input, string expected) => Assert.Equal(expected, Tenant.NormalizeSlug(input));

    [Theory]
    [InlineData("ab")]
    [InlineData("-abc")]
    [InlineData("abc-")]
    [InlineData("a b c")]
    [InlineData("ไทย")]
    [InlineData("a'; drop table--")]
    public void Rejects_invalid_slug(string input) => Assert.Throws<DomainException>(() => Tenant.NormalizeSlug(input));

    [Theory]
    [InlineData(TenantPlan.Starter, 5)]
    [InlineData(TenantPlan.Pro, 15)]
    [InlineData(TenantPlan.Enterprise, 30)]
    public void Package_sets_user_limit(TenantPlan plan, int expected) => Assert.Equal(expected, new Tenant("factory", "Factory", plan).MaxUsers);
}
