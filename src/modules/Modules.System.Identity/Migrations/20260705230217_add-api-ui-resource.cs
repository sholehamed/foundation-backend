using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class addapiuiresource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApiResource_Resources_ResourceId",
                table: "ApiResource");

            migrationBuilder.DropForeignKey(
                name: "FK_PermissionApiResources_ApiResource_ApiResourceId",
                table: "PermissionApiResources");

            migrationBuilder.DropForeignKey(
                name: "FK_PermissionUiResources_UiResource_UiResourceId",
                table: "PermissionUiResources");

            migrationBuilder.DropForeignKey(
                name: "FK_UiResource_Resources_ResourceId",
                table: "UiResource");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UiResource",
                table: "UiResource");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ApiResource",
                table: "ApiResource");

            migrationBuilder.RenameTable(
                name: "UiResource",
                newName: "UiResources");

            migrationBuilder.RenameTable(
                name: "ApiResource",
                newName: "ApiResources");

            migrationBuilder.RenameIndex(
                name: "IX_UiResource_ResourceId",
                table: "UiResources",
                newName: "IX_UiResources_ResourceId");

            migrationBuilder.RenameIndex(
                name: "IX_ApiResource_ResourceId",
                table: "ApiResources",
                newName: "IX_ApiResources_ResourceId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UiResources",
                table: "UiResources",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ApiResources",
                table: "ApiResources",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApiResources_Resources_ResourceId",
                table: "ApiResources",
                column: "ResourceId",
                principalTable: "Resources",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PermissionApiResources_ApiResources_ApiResourceId",
                table: "PermissionApiResources",
                column: "ApiResourceId",
                principalTable: "ApiResources",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PermissionUiResources_UiResources_UiResourceId",
                table: "PermissionUiResources",
                column: "UiResourceId",
                principalTable: "UiResources",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_UiResources_Resources_ResourceId",
                table: "UiResources",
                column: "ResourceId",
                principalTable: "Resources",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApiResources_Resources_ResourceId",
                table: "ApiResources");

            migrationBuilder.DropForeignKey(
                name: "FK_PermissionApiResources_ApiResources_ApiResourceId",
                table: "PermissionApiResources");

            migrationBuilder.DropForeignKey(
                name: "FK_PermissionUiResources_UiResources_UiResourceId",
                table: "PermissionUiResources");

            migrationBuilder.DropForeignKey(
                name: "FK_UiResources_Resources_ResourceId",
                table: "UiResources");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UiResources",
                table: "UiResources");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ApiResources",
                table: "ApiResources");

            migrationBuilder.RenameTable(
                name: "UiResources",
                newName: "UiResource");

            migrationBuilder.RenameTable(
                name: "ApiResources",
                newName: "ApiResource");

            migrationBuilder.RenameIndex(
                name: "IX_UiResources_ResourceId",
                table: "UiResource",
                newName: "IX_UiResource_ResourceId");

            migrationBuilder.RenameIndex(
                name: "IX_ApiResources_ResourceId",
                table: "ApiResource",
                newName: "IX_ApiResource_ResourceId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UiResource",
                table: "UiResource",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ApiResource",
                table: "ApiResource",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApiResource_Resources_ResourceId",
                table: "ApiResource",
                column: "ResourceId",
                principalTable: "Resources",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PermissionApiResources_ApiResource_ApiResourceId",
                table: "PermissionApiResources",
                column: "ApiResourceId",
                principalTable: "ApiResource",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PermissionUiResources_UiResource_UiResourceId",
                table: "PermissionUiResources",
                column: "UiResourceId",
                principalTable: "UiResource",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_UiResource_Resources_ResourceId",
                table: "UiResource",
                column: "ResourceId",
                principalTable: "Resources",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
