using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IraqiTradeCenterCompany.API.Auth.Migrations
{
    public partial class AddMediaBackupSettings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MediaBackupSettings",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    MediaRootPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IncludeDatabaseBackup = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IncludeVoucherData = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IncludeAttachments = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    AutoBackupEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    AutoBackupCron = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RetentionYears = table.Column<int>(type: "int", nullable: false, defaultValue: 5),
                    LastRunAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastRunStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Idle"),
                    LastRunError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LastRunYearFolder = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaBackupSettings", x => x.Id);
                });

            migrationBuilder.InsertData(
                schema: "auth",
                table: "MediaBackupSettings",
                columns: new[] { "Id", "IncludeDatabaseBackup", "IncludeVoucherData", "IncludeAttachments", "AutoBackupEnabled", "RetentionYears", "LastRunStatus" },
                values: new object[] { 1, true, true, true, false, 5, "Idle" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "MediaBackupSettings", schema: "auth");
        }
    }
}
