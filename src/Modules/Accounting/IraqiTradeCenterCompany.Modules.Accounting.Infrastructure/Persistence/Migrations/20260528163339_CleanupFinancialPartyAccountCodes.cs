using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IraqiTradeCenterCompany.Modules.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CleanupFinancialPartyAccountCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ‎نُزيل الـ underscore من أكواد الحسابات التي أنشأتها وحدة الإدارة المالية
            // ‎(الحسابات المرتبطة بطرف مالي). نقيّد التحديث على هذه فقط لتفادي العبث
            // ‎بأي كود حساب آخر يحتوي على underscore لأي سبب.
            migrationBuilder.Sql(@"
                UPDATE a
                SET a.Code = REPLACE(a.Code, '_', '')
                FROM acc.Accounts a
                INNER JOIN acc.FinancialParties p ON p.AccountId = a.Id
                WHERE a.Code LIKE '%[_]%';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ‎التراجع غير ممكن تلقائياً (لا نعرف موقع underscore الأصلي بعد إزالته).
        }
    }
}
