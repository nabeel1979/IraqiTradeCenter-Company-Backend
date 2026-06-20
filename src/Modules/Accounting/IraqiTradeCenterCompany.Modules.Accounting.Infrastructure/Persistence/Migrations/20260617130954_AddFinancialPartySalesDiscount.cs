using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IraqiTradeCenterCompany.Modules.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialPartySalesDiscount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SalesDiscountEnabled",
                schema: "acc",
                table: "FinancialParties",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "SalesDiscountPercentage",
                schema: "acc",
                table: "FinancialParties",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SalesDiscountEnabled",
                schema: "acc",
                table: "FinancialParties");

            migrationBuilder.DropColumn(
                name: "SalesDiscountPercentage",
                schema: "acc",
                table: "FinancialParties");
        }
    }
}
