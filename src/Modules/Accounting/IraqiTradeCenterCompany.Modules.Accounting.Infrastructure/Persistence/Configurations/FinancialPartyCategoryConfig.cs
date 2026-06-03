using IraqiTradeCenterCompany.Modules.Accounting.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IraqiTradeCenterCompany.Modules.Accounting.Infrastructure.Persistence.Configurations;

public class FinancialPartyCategoryConfig : IEntityTypeConfiguration<FinancialPartyCategory>
{
    public void Configure(EntityTypeBuilder<FinancialPartyCategory> b)
    {
        b.ToTable("FinancialPartyCategories");
        b.HasKey(x => x.Id);

        b.Property(x => x.Kind).HasConversion<int>().IsRequired();
        b.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        b.Property(x => x.NameEn).HasMaxLength(200);
        b.Property(x => x.IsActive).HasDefaultValue(true).IsRequired();
        b.Property(x => x.DisplayOrder).HasDefaultValue(100).IsRequired();

        b.HasIndex(x => new { x.Kind, x.NameAr }).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x => x.MainAccountId).IsUnique().HasFilter("[IsDeleted] = 0");

        b.HasOne(x => x.MainAccount)
            .WithMany()
            .HasForeignKey(x => x.MainAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.Parties)
            .WithOne(p => p.Category)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasQueryFilter(x => !x.IsDeleted);
    }
}
