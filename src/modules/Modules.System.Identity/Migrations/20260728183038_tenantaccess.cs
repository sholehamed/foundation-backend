using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class tenantaccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantClosures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AncestorTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DescendantTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Depth = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantClosures", x => x.Id);
                    table.CheckConstraint("CK_TenantClosures_Depth", "[Depth] >= 0");
                    table.ForeignKey(
                        name: "FK_TenantClosures_Tenants_AncestorTenantId",
                        column: x => x.AncestorTenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TenantClosures_Tenants_DescendantTenantId",
                        column: x => x.DescendantTenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserTenantAccess",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CanRead = table.Column<bool>(type: "bit", nullable: false),
                    CanWrite = table.Column<bool>(type: "bit", nullable: false),
                    IncludeDescendants = table.Column<bool>(type: "bit", nullable: false),
                    IsDenied = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByIp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedByIp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTenantAccess", x => x.Id);
                    table.CheckConstraint("CK_UserTenantAccess_ReadWrite", "[CanRead] = 1 OR [CanWrite] = 1");
                    table.ForeignKey(
                        name: "FK_UserTenantAccess_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserTenantAccess_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantClosures_AncestorTenantId_Depth",
                table: "TenantClosures",
                columns: new[] { "AncestorTenantId", "Depth" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantClosures_DescendantTenantId",
                table: "TenantClosures",
                column: "DescendantTenantId");

            migrationBuilder.CreateIndex(
                name: "IX_UserTenantAccess_TenantId",
                table: "UserTenantAccess",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_UserTenantAccess_UserId",
                table: "UserTenantAccess",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserTenantAccess_UserId_ExpiresAt",
                table: "UserTenantAccess",
                columns: new[] { "UserId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "UX_UserTenantAccess_UserId_TenantId",
                table: "UserTenantAccess",
                columns: new[] { "UserId", "TenantId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantClosures");

            migrationBuilder.DropTable(
                name: "UserTenantAccess");
        }
    }
}
