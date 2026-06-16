using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IraqiTradeCenterCompany.API.Auth.Migrations
{
    [Migration("20260603120000_AddEmailSettings")]
    public partial class AddEmailSettings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailSettings",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    Provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Zoho"),
                    SmtpHost = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: "smtp.zoho.com"),
                    SmtpPort = table.Column<int>(type: "int", nullable: false, defaultValue: 587),
                    SecurityMode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "StartTls"),
                    Username = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AppPassword = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FromEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FromDisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReplyToEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SignatureHtml = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailSettings", x => x.Id);
                });

            migrationBuilder.InsertData(
                schema: "auth",
                table: "EmailSettings",
                columns: new[] { "Id", "IsEnabled", "Provider", "SmtpHost", "SmtpPort", "SecurityMode" },
                values: new object[] { 1, false, "Zoho", "smtp.zoho.com", 587, "StartTls" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "EmailSettings", schema: "auth");
        }
    }
}
