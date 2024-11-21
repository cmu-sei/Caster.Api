using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Caster.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Added_SystemRole10 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "immutable",
                table: "system_roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.InsertData(
                table: "system_roles",
                columns: new[] { "id", "all_permissions", "description", "immutable", "name", "permissions" },
                values: new object[] { new Guid("f35e8fff-f996-4cba-b303-3ba515ad8d2f"), true, "Can perform all actions", true, "Administrator", new int[0] });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "system_roles",
                keyColumn: "id",
                keyValue: new Guid("f35e8fff-f996-4cba-b303-3ba515ad8d2f"));

            migrationBuilder.DropColumn(
                name: "immutable",
                table: "system_roles");
        }
    }
}
