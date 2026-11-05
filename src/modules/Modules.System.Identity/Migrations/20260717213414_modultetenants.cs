using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class modultetenants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TenantModule_Modules_ModuleId",
                table: "TenantModule");

            migrationBuilder.DropForeignKey(
                name: "FK_TenantModule_Tenants_TenantId",
                table: "TenantModule");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TenantModule",
                table: "TenantModule");

            migrationBuilder.RenameTable(
                name: "TenantModule",
                newName: "TenantModules");

            migrationBuilder.RenameIndex(
                name: "IX_TenantModule_TenantId",
                table: "TenantModules",
                newName: "IX_TenantModules_TenantId");

            migrationBuilder.RenameIndex(
                name: "IX_TenantModule_ModuleId",
                table: "TenantModules",
                newName: "IX_TenantModules_ModuleId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TenantModules",
                table: "TenantModules",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_TenantModules_Modules_ModuleId",
                table: "TenantModules",
                column: "ModuleId",
                principalTable: "Modules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TenantModules_Tenants_TenantId",
                table: "TenantModules",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TenantModules_Modules_ModuleId",
                table: "TenantModules");

            migrationBuilder.DropForeignKey(
                name: "FK_TenantModules_Tenants_TenantId",
                table: "TenantModules");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TenantModules",
                table: "TenantModules");

            migrationBuilder.RenameTable(
                name: "TenantModules",
                newName: "TenantModule");

            migrationBuilder.RenameIndex(
                name: "IX_TenantModules_TenantId",
                table: "TenantModule",
                newName: "IX_TenantModule_TenantId");

            migrationBuilder.RenameIndex(
                name: "IX_TenantModules_ModuleId",
                table: "TenantModule",
                newName: "IX_TenantModule_ModuleId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TenantModule",
                table: "TenantModule",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_TenantModule_Modules_ModuleId",
                table: "TenantModule",
                column: "ModuleId",
                principalTable: "Modules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TenantModule_Tenants_TenantId",
                table: "TenantModule",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
