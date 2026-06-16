using IraqiTradeCenterCompany.SharedKernel.Models;

namespace IraqiTradeCenterCompany.API.Trash;

public interface ITrashService
{
    Task<List<TrashItemDto>> ListAllAsync(CancellationToken ct);
    Task<Result> RestoreAsync(string entityType, int id, CancellationToken ct);
    Task<Result> PermanentlyDeleteAsync(string entityType, int id, CancellationToken ct);
    Task<PurgeAllResult> PurgeAllAsync(string? entityType, CancellationToken ct);
    IReadOnlyList<string> SupportedEntityTypes { get; }
}

public sealed record PurgeAllResult(int Deleted, int Failed, List<string> Errors);

/// <summary>
/// واجهة موحَّدة لسلّة المهملات عبر النظام بأكمله — تُجمّع كل المُزوِّدين المسجَّلين
/// وتدير عمليات الاستعراض/الاستعادة/الحذف النهائي بناءً على <c>EntityType</c>.
/// </summary>
public class TrashService : ITrashService
{
    private readonly IReadOnlyDictionary<string, ITrashProvider> _providers;

    public TrashService(IEnumerable<ITrashProvider> providers)
    {
        _providers = providers.ToDictionary(p => p.EntityType, p => p, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> SupportedEntityTypes => _providers.Keys.ToList();

    public async Task<List<TrashItemDto>> ListAllAsync(CancellationToken ct)
    {
        var results = new List<TrashItemDto>();
        foreach (var p in _providers.Values)
        {
            try
            {
                var items = await p.ListAsync(ct);
                results.AddRange(items);
            }
            catch (Exception ex)
            {
                // ‎لا نُسقط الطلب كله إن فشل مزوّد واحد — نضيف عنصراً يوضّح الخطأ
                // ‎للمستخدم بدل إخفاء النوع كاملاً، ولينتبه المسؤول إلى الخلل.
                // ‎مهم: نُغلق على هذا العنصر كل من الاستعادة والحذف النهائي لأنه
                // ‎عنصر وهمي بـ Id=0 — أي محاولة تنفيذ ستفشل بـ "غير موجود".
                results.Add(new TrashItemDto
                {
                    EntityType = p.EntityType,
                    EntityTypeLabel = p.EntityType,
                    Module = "خطأ",
                    Icon = "AlertTriangle",
                    EntityId = 0,
                    DisplayName = $"تعذّر تحميل سلة {p.EntityType}",
                    SubInfo = ex.Message,
                    CanRestore = false,
                    CannotRestoreReason = ex.Message,
                    CanPurge = false,
                    CannotPurgeReason = ex.Message,
                });
            }
        }
        // ‎الأحدث أولاً ضمن السلة الموحَّدة.
        return results.OrderByDescending(r => r.DeletedAt ?? DateTime.MinValue).ToList();
    }

    public Task<Result> RestoreAsync(string entityType, int id, CancellationToken ct)
    {
        if (!_providers.TryGetValue(entityType, out var p))
            return Task.FromResult(Result.Failure($"نوع غير معروف: {entityType}"));
        return p.RestoreAsync(id, ct);
    }

    public Task<Result> PermanentlyDeleteAsync(string entityType, int id, CancellationToken ct)
    {
        if (!_providers.TryGetValue(entityType, out var p))
            return Task.FromResult(Result.Failure($"نوع غير معروف: {entityType}"));
        return p.PermanentlyDeleteAsync(id, ct);
    }

    public async Task<PurgeAllResult> PurgeAllAsync(string? entityType, CancellationToken ct)
    {
        // جلب العناصر المراد حذفها (كل السلة أو نوع معين)
        var allItems = await ListAllAsync(ct);
        var targets = string.IsNullOrWhiteSpace(entityType)
            ? allItems
            : allItems.Where(i => string.Equals(i.EntityType, entityType, StringComparison.OrdinalIgnoreCase)).ToList();

        // استثناء عناصر الأخطاء وعناصر canPurge = false
        targets = targets.Where(i => i.EntityId != 0 && i.CanPurge != false).ToList();

        int deleted = 0, failed = 0;
        var errors = new List<string>();

        foreach (var item in targets)
        {
            if (!_providers.TryGetValue(item.EntityType, out var p))
            {
                failed++;
                errors.Add($"نوع غير معروف: {item.EntityType}");
                continue;
            }
            var result = await p.PermanentlyDeleteAsync(item.EntityId, ct);
            if (result.IsSuccess) deleted++;
            else
            {
                failed++;
                errors.Add($"{item.DisplayName}: {string.Join(", ", result.Errors)}");
            }
        }
        return new PurgeAllResult(deleted, failed, errors);
    }
}
