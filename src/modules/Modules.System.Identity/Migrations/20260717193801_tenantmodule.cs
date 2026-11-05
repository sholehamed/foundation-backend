using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class tenantmodule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentId",
                table: "Tenants",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TenantModule",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CustomExpiryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsTrial = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByIp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedByIp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantModule", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantModule_Modules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "Modules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TenantModule_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_ParentId",
                table: "Tenants",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantModule_ModuleId",
                table: "TenantModule",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantModule_TenantId",
                table: "TenantModule",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tenants_Tenants_ParentId",
                table: "Tenants",
                column: "ParentId",
                principalTable: "Tenants",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tenants_Tenants_ParentId",
                table: "Tenants");

            migrationBuilder.DropTable(
                name: "TenantModule");

            migrationBuilder.DropIndex(
                name: "IX_Tenants_ParentId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "Tenants");
        }
    }
}
