using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IraqiTradeCenterCompany.Modules.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttachmentSyncOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsOnLocal",
                schema: "acc",
                table: "VoucherAttachments",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsOnR2",
                schema: "acc",
                table: "VoucherAttachments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AttachmentSyncOutbox",
                schema: "acc",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AttachmentId = table.Column<int>(type: "int", nullable: false),
                    Operation = table.Column<int>(type: "int", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SyncedToR2AtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LocalPurgeAfterUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LocalPurgedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttachmentSyncOutbox", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentSyncOutbox_AttachmentId",
                schema: "acc",
                table: "AttachmentSyncOutbox",
                column: "AttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentSyncOutbox_LocalPurgeAfterUtc",
                schema: "acc",
                table: "AttachmentSyncOutbox",
                column: "LocalPurgeAfterUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentSyncOutbox_Status",
                schema: "acc",
                table: "AttachmentSyncOutbox",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentSyncOutbox_Status_Operation",
                schema: "acc",
                table: "AttachmentSyncOutbox",
                columns: new[] { "Status", "Operation" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttachmentSyncOutbox",
                schema: "acc");

            migrationBuilder.DropColumn(
                name: "IsOnLocal",
                schema: "acc",
                table: "VoucherAttachments");

            migrationBuilder.DropColumn(
                name: "IsOnR2",
                schema: "acc",
                table: "VoucherAttachments");
        }
    }
}
