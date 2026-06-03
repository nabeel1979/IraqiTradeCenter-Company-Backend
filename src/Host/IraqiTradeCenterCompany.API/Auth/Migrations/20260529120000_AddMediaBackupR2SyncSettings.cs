using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IraqiTradeCenterCompany.API.Auth.Migrations
{
    public partial class AddMediaBackupR2SyncSettings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SyncDatabaseBackupToR2",
                schema: "auth",
                table: "MediaBackupSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ServerDatabaseBackupKeepCount",
                schema: "auth",
                table: "MediaBackupSettings",
                type: "int",
                nullable: false,
                defaultValue: 3);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SyncDatabaseBackupToR2", schema: "auth", table: "MediaBackupSettings");
            migrationBuilder.DropColumn(name: "ServerDatabaseBackupKeepCount", schema: "auth", table: "MediaBackupSettings");
        }
    }
}
