using IraqiTradeCenterCompany.Modules.Accounting.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IraqiTradeCenterCompany.Modules.Accounting.Infrastructure.Persistence.Configurations;

/// <summary>
/// تكوين جدول طابور المزامنة <c>acc.AttachmentSyncOutbox</c>:
///   • مفتاح أساسي تصاعدي + فهارس على الحالة + (Operation, Status) لتسريع الـ
///     polling من الـ background service.
///   • فهرس على <c>LocalPurgeAfterUtc</c> لتسريع البحث عن العناصر التي حان
///     وقت مسحها من القرص المحلي.
/// </summary>
public class AttachmentSyncOutboxConfig : IEntityTypeConfiguration<AttachmentSyncOutbox>
{
    public void Configure(EntityTypeBuilder<AttachmentSyncOutbox> b)
    {
        b.ToTable("AttachmentSyncOutbox");
        b.HasKey(x => x.Id);

        b.Property(x => x.AttachmentId).IsRequired();
        b.Property(x => x.Operation).HasConversion<int>().IsRequired();
        b.Property(x => x.Status).HasConversion<int>().IsRequired();
        b.Property(x => x.StorageKey).HasMaxLength(500).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(150);
        b.Property(x => x.LastError).HasMaxLength(1000);

        b.HasIndex(x => x.Status);
        b.HasIndex(x => new { x.Status, x.Operation });
        b.HasIndex(x => x.LocalPurgeAfterUtc);
        b.HasIndex(x => x.AttachmentId);
    }
}
