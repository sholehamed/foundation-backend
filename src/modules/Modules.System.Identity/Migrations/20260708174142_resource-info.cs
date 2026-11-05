using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class resourceinfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Permissions_Modules_ModuleId",
                table: "Permissions");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "UiResources");

            migrationBuilder.DropColumn(
                name: "RequiresAuth",
                table: "ApiResources");

            migrationBuilder.RenameColumn(
                name: "ModuleId",
                table: "Permissions",
                newName: "ResourceId");

            migrationBuilder.RenameIndex(
                name: "IX_Permissions_ModuleId",
                table: "Permissions",
                newName: "IX_Permissions_ResourceId");

            migrationBuilder.AddForeignKey(
                name: "FK_Permissions_Resources_ResourceId",
                table: "Permissions",
                column: "ResourceId",
                principalTable: "Resources",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Permissions_Resources_ResourceId",
                table: "Permissions");

            migrationBuilder.RenameColumn(
                name: "ResourceId",
                table: "Permissions",
                newName: "ModuleId");

            migrationBuilder.RenameIndex(
                name: "IX_Permissions_ResourceId",
                table: "Permissions",
                newName: "IX_Permissions_ModuleId");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "UiResources",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "RequiresAuth",
                table: "ApiResources",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddForeignKey(
                name: "FK_Permissions_Modules_ModuleId",
                table: "Permissions",
                column: "ModuleId",
                principalTable: "Modules",
                principalColumn: "Id");
        }
    }
}
