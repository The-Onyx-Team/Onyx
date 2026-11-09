using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Onyx.Data.SQLite.Migrations
{
    /// <inheritdoc />
    public partial class UsageBuckets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Older rows may repeat (device, app, date); keep one per key so the unique index can be created.
            migrationBuilder.Sql(@"DELETE FROM ""Usage"" WHERE rowid NOT IN (SELECT MAX(rowid) FROM ""Usage"" GROUP BY ""DeviceId"", ""AppId"", ""Date"");");

            migrationBuilder.DropForeignKey(
                name: "FK_Groups_AspNetUsers_AdminId",
                table: "Groups");

            migrationBuilder.DropForeignKey(
                name: "FK_Usage_Categories_CategoryId",
                table: "Usage");

            migrationBuilder.DropIndex(
                name: "IX_Usage_DeviceId",
                table: "Usage");

            migrationBuilder.AlterColumn<int>(
                name: "CategoryId",
                table: "Usage",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "RegisteredApps",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Identifier",
                table: "RegisteredApps",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSeen",
                table: "Devices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "Devices",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Usage_DeviceId_AppId_Date",
                table: "Usage",
                columns: new[] { "DeviceId", "AppId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RegisteredApps_CategoryId",
                table: "RegisteredApps",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_RegisteredApps_Identifier",
                table: "RegisteredApps",
                column: "Identifier",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Groups_AspNetUsers_AdminId",
                table: "Groups",
                column: "AdminId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RegisteredApps_Categories_CategoryId",
                table: "RegisteredApps",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Usage_Categories_CategoryId",
                table: "Usage",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Groups_AspNetUsers_AdminId",
                table: "Groups");

            migrationBuilder.DropForeignKey(
                name: "FK_RegisteredApps_Categories_CategoryId",
                table: "RegisteredApps");

            migrationBuilder.DropForeignKey(
                name: "FK_Usage_Categories_CategoryId",
                table: "Usage");

            migrationBuilder.DropIndex(
                name: "IX_Usage_DeviceId_AppId_Date",
                table: "Usage");

            migrationBuilder.DropIndex(
                name: "IX_RegisteredApps_CategoryId",
                table: "RegisteredApps");

            migrationBuilder.DropIndex(
                name: "IX_RegisteredApps_Identifier",
                table: "RegisteredApps");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "RegisteredApps");

            migrationBuilder.DropColumn(
                name: "Identifier",
                table: "RegisteredApps");

            migrationBuilder.DropColumn(
                name: "LastSeen",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "Devices");

            migrationBuilder.AlterColumn<int>(
                name: "CategoryId",
                table: "Usage",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Usage_DeviceId",
                table: "Usage",
                column: "DeviceId");

            migrationBuilder.AddForeignKey(
                name: "FK_Groups_AspNetUsers_AdminId",
                table: "Groups",
                column: "AdminId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Usage_Categories_CategoryId",
                table: "Usage",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
