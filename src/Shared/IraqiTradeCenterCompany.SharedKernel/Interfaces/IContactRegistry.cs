namespace IraqiTradeCenterCompany.SharedKernel.Interfaces;

/// <summary>سجل مركزي للبريد وأرقام الهاتف — يمنع التكرار بين المستخدمين وأطراف الإدارة المالية.</summary>
public interface IContactRegistry
{
    Task<IReadOnlyList<ContactPointDto>> GetForOwnerAsync(string ownerType, string ownerId, CancellationToken ct = default);

    /// <summary>يُحدّث جهات اتصال المالك بعد التحقق من عدم التكرار. القيم الفارغة تُحذف.</summary>
    Task<(bool Success, string? Error)> SyncOwnerAsync(
        string ownerType,
        string ownerId,
        string? email,
        string? phone,
        string? mobile,
        CancellationToken ct = default);

    Task<(bool Available, string? Error)> CheckAvailabilityAsync(
        string kind,
        string? value,
        string ownerType,
        string ownerId,
        CancellationToken ct = default);
}

public sealed class ContactPointDto
{
    public long Id { get; init; }
    public string Kind { get; init; } = "";
    public string DisplayValue { get; init; } = "";
    public string OwnerType { get; init; } = "";
    public string OwnerId { get; init; } = "";
    public string? OwnerLabel { get; init; }
}
