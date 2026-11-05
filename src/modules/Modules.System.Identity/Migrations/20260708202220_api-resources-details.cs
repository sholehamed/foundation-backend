using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class apiresourcesdetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApiResource_Method_Route",
                table: "ApiResources");

            migrationBuilder.CreateIndex(
                name: "IX_Resource_ApiResource_Method_Route",
                table: "ApiResources",
                columns: new[] { "ResourceId", "HttpMethod", "RouteTemplate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Resource_ApiResource_Method_Route",
                table: "ApiResources");

            migrationBuilder.CreateIndex(
                name: "IX_ApiResource_Method_Route",
                table: "ApiResources",
                columns: new[] { "HttpMethod", "RouteTemplate" },
                unique: true);
        }
    }
}
