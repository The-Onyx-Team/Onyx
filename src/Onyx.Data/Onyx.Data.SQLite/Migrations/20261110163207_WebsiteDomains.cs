using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Onyx.Data.SQLite.Migrations
{
    /// <inheritdoc />
    public partial class WebsiteDomains : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Usage_DeviceId_AppId_Date",
                table: "Usage");

            migrationBuilder.AddColumn<string>(
                name: "Domain",
                table: "Usage",
                type: "TEXT",
                maxLength: 253,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "WebsiteExclusions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Domain = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebsiteExclusions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebsiteExclusions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Usage_DeviceId_AppId_Domain_Date",
                table: "Usage",
                columns: new[] { "DeviceId", "AppId", "Domain", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebsiteExclusions_UserId_Domain",
                table: "WebsiteExclusions",
                columns: new[] { "UserId", "Domain" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebsiteExclusions");

            migrationBuilder.DropIndex(
                name: "IX_Usage_DeviceId_AppId_Domain_Date",
                table: "Usage");

            migrationBuilder.DropColumn(
                name: "Domain",
                table: "Usage");

            migrationBuilder.CreateIndex(
                name: "IX_Usage_DeviceId_AppId_Date",
                table: "Usage",
                columns: new[] { "DeviceId", "AppId", "Date" },
                unique: true);
        }
    }
}
