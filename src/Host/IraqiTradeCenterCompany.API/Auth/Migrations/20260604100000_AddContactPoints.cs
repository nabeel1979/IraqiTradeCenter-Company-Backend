using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IraqiTradeCenterCompany.API.Auth.Migrations
{
    [Migration("20260604100000_AddContactPoints")]
    public partial class AddContactPoints : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContactPoints",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    NormalizedValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OwnerType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OwnerId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactPoints", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContactPoints_Kind_NormalizedValue",
                schema: "auth",
                table: "ContactPoints",
                columns: new[] { "Kind", "NormalizedValue" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContactPoints_OwnerType_OwnerId",
                schema: "auth",
                table: "ContactPoints",
                columns: new[] { "OwnerType", "OwnerId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ContactPoints", schema: "auth");
        }
    }
}
