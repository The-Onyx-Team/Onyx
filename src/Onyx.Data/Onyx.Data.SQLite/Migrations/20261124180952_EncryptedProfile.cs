using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Onyx.Data.SQLite.Migrations
{
    /// <inheritdoc />
    public partial class EncryptedProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "Profile",
                table: "UserVaults",
                type: "BLOB",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Profile",
                table: "UserVaults");
        }
    }
}
