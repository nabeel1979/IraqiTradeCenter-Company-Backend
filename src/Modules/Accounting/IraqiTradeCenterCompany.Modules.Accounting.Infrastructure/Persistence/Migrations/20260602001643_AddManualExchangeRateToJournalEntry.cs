using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IraqiTradeCenterCompany.Modules.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManualExchangeRateToJournalEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ManualExchangeRate",
                schema: "acc",
                table: "JournalEntries",
                type: "decimal(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ManualExchangeRateOperation",
                schema: "acc",
                table: "JournalEntries",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ManualExchangeRate",
                schema: "acc",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "ManualExchangeRateOperation",
                schema: "acc",
                table: "JournalEntries");
        }
    }
}
