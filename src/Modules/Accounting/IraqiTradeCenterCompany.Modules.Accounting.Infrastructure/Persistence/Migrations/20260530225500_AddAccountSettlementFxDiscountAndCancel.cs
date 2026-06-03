using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IraqiTradeCenterCompany.Modules.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountSettlementFxDiscountAndCancel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FxDiscountAccountId",
                schema: "acc",
                table: "AccountSettlementSettings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                schema: "acc",
                table: "AccountSettlements",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FxDiscountAccountId",
                schema: "acc",
                table: "AccountSettlements",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FxDiscountAmount",
                schema: "acc",
                table: "AccountSettlements",
                type: "decimal(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "SourceReversalJournalEntryId",
                schema: "acc",
                table: "AccountSettlements",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetReversalJournalEntryId",
                schema: "acc",
                table: "AccountSettlements",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FxDiscountAccountId",
                schema: "acc",
                table: "AccountSettlementSettings");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                schema: "acc",
                table: "AccountSettlements");

            migrationBuilder.DropColumn(
                name: "FxDiscountAccountId",
                schema: "acc",
                table: "AccountSettlements");

            migrationBuilder.DropColumn(
                name: "FxDiscountAmount",
                schema: "acc",
                table: "AccountSettlements");

            migrationBuilder.DropColumn(
                name: "SourceReversalJournalEntryId",
                schema: "acc",
                table: "AccountSettlements");

            migrationBuilder.DropColumn(
                name: "TargetReversalJournalEntryId",
                schema: "acc",
                table: "AccountSettlements");
        }
    }
}
