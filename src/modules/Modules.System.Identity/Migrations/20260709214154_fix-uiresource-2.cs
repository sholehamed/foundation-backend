using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class fixuiresource2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "UiResources",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Type",
                table: "UiResources");
        }
    }
}
