using IraqiTradeCenterCompany.API.Auth.Permissions;
using IraqiTradeCenterCompany.Modules.Accounting.Application.Features.AccountSettlements;
using IraqiTradeCenterCompany.Modules.Accounting.Application.Features.ManageFinancialParties;
using IraqiTradeCenterCompany.Modules.Accounting.Application.Features.ManageFinancialPartyCategories;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IraqiTradeCenterCompany.API.Controllers;

[Authorize]
[Route("api/financial-management")]
public class FinancialManagementController : BaseApiController
{
    private static class Perm
    {
        public const string CatRead   = PermissionRegistry.FinancialManagement.Categories.Read;
        public const string CatCreate = PermissionRegistry.FinancialManagement.Categories.Create;
        public const string CatUpdate = PermissionRegistry.FinancialManagement.Categories.Update;
        public const string CatDelete = PermissionRegistry.FinancialManagement.Categories.Delete;
        public const string PartyRead   = PermissionRegistry.FinancialManagement.Parties.Read;
        public const string PartyCreate = PermissionRegistry.FinancialManagement.Parties.Create;
        public const string PartyUpdate = PermissionRegistry.FinancialManagement.Parties.Update;
        public const string PartyDelete = PermissionRegistry.FinancialManagement.Parties.Delete;
        public const string BoxRead   = PermissionRegistry.Accounting.CashBoxes.Read;
        public const string BoxCreate = PermissionRegistry.Accounting.CashBoxes.Create;
        public const string BoxUpdate = PermissionRegistry.Accounting.CashBoxes.Update;
        public const string BoxDelete = PermissionRegistry.Accounting.CashBoxes.Delete;
        public const string SettleRead   = PermissionRegistry.FinancialManagement.AccountSettlements.Read;
        public const string SettleCreate = PermissionRegistry.FinancialManagement.AccountSettlements.Create;
        public const string SettleUpdate = PermissionRegistry.FinancialManagement.AccountSettlements.Update;
        public const string SettleCancel = PermissionRegistry.FinancialManagement.AccountSettlements.Cancel;
    }

    // ── Eligible accounts (leaf, no journal lines, not locked) ──────

    [HttpGet("eligible-accounts")]
    [RequireAnyPermission(Perm.CatCreate, Perm.BoxCreate)]
    public async Task<IActionResult> GetEligibleAccounts()
    {
        var data = await Mediator.Send(new GetEligibleAccountsQuery());
        return Ok(new { success = true, data });
    }

    // ── Categories ──────────────────────────────────────────────────

    [HttpGet("categories")]
    [RequireAnyPermission(Perm.CatRead, Perm.BoxRead)]
    public async Task<IActionResult> GetCategories(
        [FromQuery] FinancialPartyKind? kind = null,
        [FromQuery] bool includeInactive = false)
    {
        var data = await Mediator.Send(new GetFinancialPartyCategoriesQuery(kind, includeInactive));
        return Ok(new { success = true, data });
    }

    [HttpPost("categories")]
    [RequireAnyPermission(Perm.CatCreate, Perm.BoxCreate)]
    public async Task<IActionResult> CreateCategory([FromBody] CreateFinancialPartyCategoryCommand cmd)
        => HandleResult(await Mediator.Send(cmd));

    [HttpPut("categories/{id:int}")]
    [RequireAnyPermission(Perm.CatUpdate, Perm.BoxUpdate)]
    public async Task<IActionResult> UpdateCategory(int id, [FromBody] UpdateFinancialPartyCategoryCommand body)
    {
        var cmd = body with { Id = id };
        return HandleResult(await Mediator.Send(cmd));
    }

    [HttpDelete("categories/{id:int}")]
    [RequireAnyPermission(Perm.CatDelete, Perm.BoxDelete)]
    public async Task<IActionResult> DeleteCategory(int id)
        => HandleResult(await Mediator.Send(new DeleteFinancialPartyCategoryCommand(id)));

    // ── Parties ─────────────────────────────────────────────────────

    [HttpGet("parties")]
    [RequireAnyPermission(Perm.PartyRead, Perm.BoxRead)]
    public async Task<IActionResult> GetParties(
        [FromQuery] FinancialPartyKind? kind = null,
        [FromQuery] int? categoryId = null,
        [FromQuery] bool includeInactive = false,
        [FromQuery] string? search = null)
    {
        var data = await Mediator.Send(new GetFinancialPartiesQuery(kind, categoryId, includeInactive, search));
        return Ok(new { success = true, data });
    }

    [HttpPost("parties")]
    [RequireAnyPermission(Perm.PartyCreate, Perm.BoxCreate)]
    public async Task<IActionResult> CreateParty([FromBody] CreateFinancialPartyCommand cmd)
        => HandleResult(await Mediator.Send(cmd));

    [HttpPut("parties/{id:int}")]
    [RequireAnyPermission(Perm.PartyUpdate, Perm.BoxUpdate)]
    public async Task<IActionResult> UpdateParty(int id, [FromBody] UpdateFinancialPartyCommand body)
    {
        var cmd = body with { Id = id };
        return HandleResult(await Mediator.Send(cmd));
    }

    [HttpDelete("parties/{id:int}")]
    [RequireAnyPermission(Perm.PartyDelete, Perm.BoxDelete)]
    public async Task<IActionResult> DeleteParty(int id)
        => HandleResult(await Mediator.Send(new DeleteFinancialPartyCommand(id)));

    // ── Account Settlements (تسوية حسابات) ───────────────────────────

    [HttpGet("account-settlements/settings")]
    [RequireAnyPermission(Perm.SettleRead, Perm.SettleUpdate)]
    public async Task<IActionResult> GetSettlementSettings()
    {
        var data = await Mediator.Send(new GetAccountSettlementSettingsQuery());
        return Ok(new { success = true, data });
    }

    [HttpPut("account-settlements/settings")]
    [RequirePermission(Perm.SettleUpdate)]
    public async Task<IActionResult> UpdateSettlementSettings([FromBody] UpdateAccountSettlementSettingsCommand cmd)
        => HandleResult(await Mediator.Send(cmd));

    [HttpGet("account-settlements")]
    [RequirePermission(Perm.SettleRead)]
    public async Task<IActionResult> ListSettlements([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var data = await Mediator.Send(new GetAccountSettlementsQuery(from, to));
        return Ok(new { success = true, data });
    }

    [HttpPost("account-settlements/preview")]
    [RequireAnyPermission(Perm.SettleRead, Perm.SettleCreate)]
    public async Task<IActionResult> PreviewSettlement([FromBody] PreviewAccountSettlementQuery query)
        => HandleResult(await Mediator.Send(query));

    [HttpPost("account-settlements")]
    [RequirePermission(Perm.SettleCreate)]
    public async Task<IActionResult> CreateSettlement([FromBody] CreateAccountSettlementCommand cmd)
        => HandleResult(await Mediator.Send(cmd));

    [HttpPost("account-settlements/{id:int}/cancel")]
    [RequirePermission(Perm.SettleCancel)]
    public async Task<IActionResult> CancelSettlement(int id, [FromBody] CancelAccountSettlementDto body)
        => HandleResult(await Mediator.Send(new CancelAccountSettlementCommand(id, body)));

    [HttpDelete("account-settlements/{id:int}")]
    [RequirePermission(Perm.SettleCancel)]
    public async Task<IActionResult> DeleteSettlement(int id)
        => HandleResult(await Mediator.Send(new DeleteAccountSettlementCommand(id)));

    [HttpGet("account-settlements/transit-movements")]
    [RequirePermission(Perm.SettleRead)]
    public async Task<IActionResult> GetTransitMovements(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string? currency, [FromQuery] int? transitAccountId)
    {
        var data = await Mediator.Send(new GetAccountSettlementTransitMovementsQuery(
            from, to, currency, transitAccountId));
        return Ok(new { success = true, data });
    }
}
