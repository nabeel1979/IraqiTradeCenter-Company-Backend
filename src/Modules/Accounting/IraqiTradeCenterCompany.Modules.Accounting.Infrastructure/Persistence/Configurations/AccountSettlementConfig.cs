using IraqiTradeCenterCompany.Modules.Accounting.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IraqiTradeCenterCompany.Modules.Accounting.Infrastructure.Persistence.Configurations;

public class AccountSettlementConfig : IEntityTypeConfiguration<AccountSettlement>
{
    public void Configure(EntityTypeBuilder<AccountSettlement> b)
    {
        b.ToTable("AccountSettlements");
        b.HasKey(x => x.Id);
        b.Property(x => x.SettlementNumber).HasMaxLength(32).IsRequired();
        b.HasIndex(x => x.SettlementNumber).IsUnique().HasFilter("[IsDeleted] = 0");
        b.Property(x => x.SourceCurrency).HasMaxLength(8).IsRequired();
        b.Property(x => x.TargetCurrency).HasMaxLength(8).IsRequired();
        b.Property(x => x.SourceAmount).HasColumnType("decimal(18,3)");
        b.Property(x => x.TargetAmount).HasColumnType("decimal(18,3)");
        b.Property(x => x.ExchangeRate).HasColumnType("decimal(18,6)");
        b.Property(x => x.FxGainLossAmount).HasColumnType("decimal(18,3)");
        b.Property(x => x.FxDiscountAmount).HasColumnType("decimal(18,3)");
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.CancelReason).HasMaxLength(500);

        b.HasOne(x => x.SourceAccount).WithMany().HasForeignKey(x => x.SourceAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.TargetAccount).WithMany().HasForeignKey(x => x.TargetAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SourceTransitAccount).WithMany().HasForeignKey(x => x.SourceTransitAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.TargetTransitAccount).WithMany().HasForeignKey(x => x.TargetTransitAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SourceJournalEntry).WithMany().HasForeignKey(x => x.SourceJournalEntryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.TargetJournalEntry).WithMany().HasForeignKey(x => x.TargetJournalEntryId).OnDelete(DeleteBehavior.Restrict);

        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

public class AccountSettlementSettingsConfig : IEntityTypeConfiguration<AccountSettlementSettings>
{
    public void Configure(EntityTypeBuilder<AccountSettlementSettings> b)
    {
        b.ToTable("AccountSettlementSettings");
        b.HasKey(x => x.Id);
        b.Property(x => x.TransitAccountsJson).HasColumnType("nvarchar(max)");
    }
}
