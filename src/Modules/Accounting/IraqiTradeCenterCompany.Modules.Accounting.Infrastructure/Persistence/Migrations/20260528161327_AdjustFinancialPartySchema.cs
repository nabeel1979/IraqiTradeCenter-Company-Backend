using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IraqiTradeCenterCompany.Modules.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdjustFinancialPartySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FinancialParties_CategoryId_NameAr",
                schema: "acc",
                table: "FinancialParties");

            migrationBuilder.DropColumn(
                name: "CreditLimit",
                schema: "acc",
                table: "FinancialParties");

            migrationBuilder.DropColumn(
                name: "NameAr",
                schema: "acc",
                table: "FinancialParties");

            migrationBuilder.DropColumn(
                name: "NameEn",
                schema: "acc",
                table: "FinancialParties");

            migrationBuilder.AddColumn<string>(
                name: "CreditLimits",
                schema: "acc",
                table: "FinancialParties",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialParties_CategoryId",
                schema: "acc",
                table: "FinancialParties",
                column: "CategoryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FinancialParties_CategoryId",
                schema: "acc",
                table: "FinancialParties");

            migrationBuilder.DropColumn(
                name: "CreditLimits",
                schema: "acc",
                table: "FinancialParties");

            migrationBuilder.AddColumn<decimal>(
                name: "CreditLimit",
                schema: "acc",
                table: "FinancialParties",
                type: "decimal(18,3)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameAr",
                schema: "acc",
                table: "FinancialParties",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NameEn",
                schema: "acc",
                table: "FinancialParties",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialParties_CategoryId_NameAr",
                schema: "acc",
                table: "FinancialParties",
                columns: new[] { "CategoryId", "NameAr" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }
    }
}
