using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class addapiuiresourceconfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
    name: "RowVersion",
    table: "UiResources");
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "UiResources",
                type: "rowversion",
                rowVersion: true,
                nullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "LastModifiedByIp",
                table: "UiResources",
                type: "nvarchar(45)",
                maxLength: 45,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CreatedByIp",
                table: "UiResources",
                type: "nvarchar(45)",
                maxLength: 45,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);
            migrationBuilder.DropColumn(
   name: "RowVersion",
   table: "ApiResources");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "ApiResources",
                type: "rowversion",
                rowVersion: true,
                nullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "LastModifiedByIp",
                table: "ApiResources",
                type: "nvarchar(45)",
                maxLength: 45,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CreatedByIp",
                table: "ApiResources",
                type: "nvarchar(45)",
                maxLength: 45,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UiResources_CreatedAt",
                table: "UiResources",
                column: "CreatedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_ApiResources_CreatedAt",
                table: "ApiResources",
                column: "CreatedAt",
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UiResources_CreatedAt",
                table: "UiResources");

            migrationBuilder.DropIndex(
                name: "IX_ApiResources_CreatedAt",
                table: "ApiResources");
            migrationBuilder.DropColumn(
    name: "RowVersion",
    table: "UiResources");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "UiResources",
                type: "varbinary(max)",
                nullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "LastModifiedByIp",
                table: "UiResources",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(45)",
                oldMaxLength: 45,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CreatedByIp",
                table: "UiResources",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(45)",
                oldMaxLength: 45,
                oldNullable: true);

            migrationBuilder.DropColumn(
   name: "RowVersion",
   table: "ApiResources");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "ApiResources",
                type: "varbinary(max)",
                nullable: false);
           

            migrationBuilder.AlterColumn<string>(
                name: "LastModifiedByIp",
                table: "ApiResources",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(45)",
                oldMaxLength: 45,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CreatedByIp",
                table: "ApiResources",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(45)",
                oldMaxLength: 45,
                oldNullable: true);
        }
    }
}
