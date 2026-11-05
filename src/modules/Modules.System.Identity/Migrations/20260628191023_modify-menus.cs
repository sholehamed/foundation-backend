using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class modifymenus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Menus_Menus_PMenuId",
                table: "Menus");

            migrationBuilder.RenameColumn(
                name: "Url",
                table: "Menus",
                newName: "Route");

            migrationBuilder.RenameColumn(
                name: "PMenuId",
                table: "Menus",
                newName: "ParentId");

            migrationBuilder.RenameIndex(
                name: "IX_Menus_PMenuId",
                table: "Menus",
                newName: "IX_Menus_ParentId");

            migrationBuilder.AddColumn<string>(
                name: "ExternalUrl",
                table: "Menus",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AlterColumn<short>(
                name: "Order",
                table: "MenuCategories",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddForeignKey(
                name: "FK_Menus_Menus_ParentId",
                table: "Menus",
                column: "ParentId",
                principalTable: "Menus",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Menus_Menus_ParentId",
                table: "Menus");

            migrationBuilder.DropColumn(
                name: "ExternalUrl",
                table: "Menus");

            migrationBuilder.RenameColumn(
                name: "Route",
                table: "Menus",
                newName: "Url");

            migrationBuilder.RenameColumn(
                name: "ParentId",
                table: "Menus",
                newName: "PMenuId");

            migrationBuilder.RenameIndex(
                name: "IX_Menus_ParentId",
                table: "Menus",
                newName: "IX_Menus_PMenuId");

            migrationBuilder.AlterColumn<int>(
                name: "Order",
                table: "MenuCategories",
                type: "int",
                nullable: false,
                oldClrType: typeof(short),
                oldType: "smallint");

            migrationBuilder.AddForeignKey(
                name: "FK_Menus_Menus_PMenuId",
                table: "Menus",
                column: "PMenuId",
                principalTable: "Menus",
                principalColumn: "Id");
        }
    }
}
