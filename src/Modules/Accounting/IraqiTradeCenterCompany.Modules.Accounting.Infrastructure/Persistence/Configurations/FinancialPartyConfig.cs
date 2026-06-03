using IraqiTradeCenterCompany.Modules.Accounting.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IraqiTradeCenterCompany.Modules.Accounting.Infrastructure.Persistence.Configurations;

public class FinancialPartyConfig : IEntityTypeConfiguration<FinancialParty>
{
    public void Configure(EntityTypeBuilder<FinancialParty> b)
    {
        b.ToTable("FinancialParties");
        b.HasKey(x => x.Id);

        b.Property(x => x.CreditLimits).HasColumnType("nvarchar(max)");
        b.Property(x => x.AllowedCurrencies).HasColumnType("nvarchar(max)");
        b.Property(x => x.CurrencyIbans).HasColumnType("nvarchar(max)");
        b.Property(x => x.Phone).HasMaxLength(50);
        b.Property(x => x.Mobile).HasMaxLength(50);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.AddressEn).HasMaxLength(500);
        b.Property(x => x.ContactPerson).HasMaxLength(200);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.BankAccountNumber).HasMaxLength(64);
        b.Property(x => x.SwiftCode).HasMaxLength(32);
        b.Property(x => x.IsActive).HasDefaultValue(true).IsRequired();

        // ‎كل طرف يقابل حساباً واحداً فقط — نمنع التكرار على مستوى الحساب.
        b.HasIndex(x => x.AccountId).IsUnique().HasFilter("[IsDeleted] = 0");

        b.HasOne(x => x.Account)
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasQueryFilter(x => !x.IsDeleted);
    }
}
