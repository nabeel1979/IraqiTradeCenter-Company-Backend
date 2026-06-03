using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IraqiTradeCenterCompany.API.Auth.Migrations
{
    public partial class AddR2DatabaseBackupKeepCount : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "R2DatabaseBackupKeepCount",
                schema: "auth",
                table: "MediaBackupSettings",
                type: "int",
                nullable: false,
                defaultValue: 10);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "R2DatabaseBackupKeepCount",
                schema: "auth",
                table: "MediaBackupSettings");
        }
    }
}
