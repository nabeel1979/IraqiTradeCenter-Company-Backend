using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IraqiTradeCenterCompany.Modules.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountSettlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsExcludedFromReports",
                schema: "acc",
                table: "Accounts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AccountSettlements",
                schema: "acc",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SettlementNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceAccountId = table.Column<int>(type: "int", nullable: false),
                    SourceCurrency = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    SourceAmount = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    TargetAccountId = table.Column<int>(type: "int", nullable: false),
                    TargetCurrency = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    TargetAmount = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    SourceTransitAccountId = table.Column<int>(type: "int", nullable: false),
                    TargetTransitAccountId = table.Column<int>(type: "int", nullable: false),
                    FxGainLossAmount = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    FxGainLossAccountId = table.Column<int>(type: "int", nullable: true),
                    SettlementDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SourceJournalEntryId = table.Column<int>(type: "int", nullable: false),
                    TargetJournalEntryId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountSettlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountSettlements_Accounts_SourceAccountId",
                        column: x => x.SourceAccountId,
                        principalSchema: "acc",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountSettlements_Accounts_SourceTransitAccountId",
                        column: x => x.SourceTransitAccountId,
                        principalSchema: "acc",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountSettlements_Accounts_TargetAccountId",
                        column: x => x.TargetAccountId,
                        principalSchema: "acc",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountSettlements_Accounts_TargetTransitAccountId",
                        column: x => x.TargetTransitAccountId,
                        principalSchema: "acc",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountSettlements_JournalEntries_SourceJournalEntryId",
                        column: x => x.SourceJournalEntryId,
                        principalSchema: "acc",
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountSettlements_JournalEntries_TargetJournalEntryId",
                        column: x => x.TargetJournalEntryId,
                        principalSchema: "acc",
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccountSettlementSettings",
                schema: "acc",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransitAccountsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FxGainAccountId = table.Column<int>(type: "int", nullable: true),
                    FxLossAccountId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountSettlementSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountSettlements_SettlementNumber",
                schema: "acc",
                table: "AccountSettlements",
                column: "SettlementNumber",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AccountSettlements_SourceAccountId",
                schema: "acc",
                table: "AccountSettlements",
                column: "SourceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountSettlements_SourceJournalEntryId",
                schema: "acc",
                table: "AccountSettlements",
                column: "SourceJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountSettlements_SourceTransitAccountId",
                schema: "acc",
                table: "AccountSettlements",
                column: "SourceTransitAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountSettlements_TargetAccountId",
                schema: "acc",
                table: "AccountSettlements",
                column: "TargetAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountSettlements_TargetJournalEntryId",
                schema: "acc",
                table: "AccountSettlements",
                column: "TargetJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountSettlements_TargetTransitAccountId",
                schema: "acc",
                table: "AccountSettlements",
                column: "TargetTransitAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountSettlements",
                schema: "acc");

            migrationBuilder.DropTable(
                name: "AccountSettlementSettings",
                schema: "acc");

            migrationBuilder.DropColumn(
                name: "IsExcludedFromReports",
                schema: "acc",
                table: "Accounts");
        }
    }
}
