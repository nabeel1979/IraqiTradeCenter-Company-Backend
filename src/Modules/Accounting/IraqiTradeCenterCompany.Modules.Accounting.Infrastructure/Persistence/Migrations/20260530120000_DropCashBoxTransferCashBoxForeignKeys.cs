using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IraqiTradeCenterCompany.Modules.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropCashBoxTransferCashBoxForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CashBoxTransfers_CashBoxes_FromCashBoxId",
                schema: "acc",
                table: "CashBoxTransfers");

            migrationBuilder.DropForeignKey(
                name: "FK_CashBoxTransfers_CashBoxes_ToCashBoxId",
                schema: "acc",
                table: "CashBoxTransfers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_CashBoxTransfers_CashBoxes_FromCashBoxId",
                schema: "acc",
                table: "CashBoxTransfers",
                column: "FromCashBoxId",
                principalSchema: "acc",
                principalTable: "CashBoxes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CashBoxTransfers_CashBoxes_ToCashBoxId",
                schema: "acc",
                table: "CashBoxTransfers",
                column: "ToCashBoxId",
                principalSchema: "acc",
                principalTable: "CashBoxes",
                principalColumn: "Id");
        }
    }
}
