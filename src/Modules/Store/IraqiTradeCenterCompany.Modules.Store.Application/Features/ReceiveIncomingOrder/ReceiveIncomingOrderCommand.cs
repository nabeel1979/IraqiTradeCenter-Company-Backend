using IraqiTradeCenterCompany.Modules.Store.Domain.Entities;
using IraqiTradeCenterCompany.Modules.Store.Application.Persistence;
using IraqiTradeCenterCompany.SharedKernel.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.Modules.Store.Application.Features.ReceiveIncomingOrder;

public record ReceiveIncomingOrderItemDto(
    int ItemId,
    string ItemName,
    int UnitId,
    decimal Quantity,
    decimal UnitPrice);

public record ReceiveIncomingOrderCustomerDto(
    Guid PlatformUserId,
    string Code,
    string BusinessName,
    string OwnerName,
    string Phone,
    string? Email);

public record ReceiveIncomingOrderCommand(
    Guid PlatformOrderId,
    string PlatformOrderNumber,
    Guid PlatformTraderId,
    decimal TotalAmount,
    IReadOnlyList<ReceiveIncomingOrderItemDto> Items,
    ReceiveIncomingOrderCustomerDto? CustomerIfMissing) : IRequest<Result<ReceiveIncomingOrderResult>>;

public sealed class ReceiveIncomingOrderResult
{
    public int IncomingOrderId { get; init; }
    public int CustomerId { get; init; }
    public bool Created { get; init; }
}

public class ReceiveIncomingOrderHandler : IRequestHandler<ReceiveIncomingOrderCommand, Result<ReceiveIncomingOrderResult>>
{
    private readonly IStoreDbContext _store;

    public ReceiveIncomingOrderHandler(IStoreDbContext store) => _store = store;

    public async Task<Result<ReceiveIncomingOrderResult>> Handle(ReceiveIncomingOrderCommand req, CancellationToken ct)
    {
        if (req.PlatformOrderId == Guid.Empty)
            return Result.Failure<ReceiveIncomingOrderResult>("معرّف الطلبية من المنصة الأم مطلوب");
        if (string.IsNullOrWhiteSpace(req.PlatformOrderNumber))
            return Result.Failure<ReceiveIncomingOrderResult>("رقم الطلبية من المنصة الأم مطلوب");
        if (req.Items is not { Count: > 0 })
            return Result.Failure<ReceiveIncomingOrderResult>("يجب أن تحتوي الطلبية على صنف واحد على الأقل");

        var existing = await _store.IncomingOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.PlatformOrderId == req.PlatformOrderId, ct);
        if (existing is not null)
        {
            return Result.Success(new ReceiveIncomingOrderResult
            {
                IncomingOrderId = existing.Id,
                CustomerId = existing.CustomerId,
                Created = false,
            });
        }

        var customer = await _store.Customers
            .FirstOrDefaultAsync(c => c.PlatformTraderId == req.PlatformTraderId, ct);

        if (customer is null)
        {
            if (req.CustomerIfMissing is null)
                return Result.Failure<ReceiveIncomingOrderResult>(
                    "العميل غير مربوط بهذه الشركة — أرسل بيانات العميل (CustomerIfMissing)");

            var c = req.CustomerIfMissing;
            customer = Customer.Create(
                c.PlatformUserId,
                req.PlatformTraderId,
                c.Code.Trim(),
                c.BusinessName.Trim(),
                c.OwnerName.Trim(),
                c.Phone.Trim());
            if (!string.IsNullOrWhiteSpace(c.Email))
            {
                // Email not in factory — skip or extend later
            }
            _store.Customers.Add(customer);
            await _store.SaveChangesAsync(ct);
        }

        var order = IncomingOrder.Receive(
            req.PlatformOrderId,
            req.PlatformOrderNumber.Trim(),
            customer.Id,
            req.TotalAmount);

        foreach (var line in req.Items)
        {
            order.AddItem(
                line.ItemId,
                line.ItemName.Trim(),
                line.UnitId,
                line.Quantity,
                line.UnitPrice);
        }

        _store.IncomingOrders.Add(order);
        await _store.SaveChangesAsync(ct);

        return Result<ReceiveIncomingOrderResult>.Success(new ReceiveIncomingOrderResult
        {
            IncomingOrderId = order.Id,
            CustomerId = customer.Id,
            Created = true,
        });
    }
}
