using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class updaterolescope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Roles_TenantId_NormalizedName",
                table: "Roles");

            migrationBuilder.RenameColumn(
                name: "UserTpe",
                table: "Users",
                newName: "UserType");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "Roles",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<byte>(
                name: "Scope",
                table: "Roles",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.CreateIndex(
                name: "IX_Roles_TenantId_NormalizedName",
                table: "Roles",
                columns: new[] { "TenantId", "NormalizedName" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Roles_TenantId_NormalizedName",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "Scope",
                table: "Roles");

            migrationBuilder.RenameColumn(
                name: "UserType",
                table: "Users",
                newName: "UserTpe");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "Roles",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Roles_TenantId_NormalizedName",
                table: "Roles",
                columns: new[] { "TenantId", "NormalizedName" },
                unique: true);
        }
    }
}
