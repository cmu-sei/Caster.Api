using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Caster.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Added_SystemRole13 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "system_roles",
                keyColumn: "id",
                keyValue: new Guid("f35e8fff-f996-4cba-b303-3ba515ad8d2f"),
                column: "description",
                value: "Can perform all actions.");

            migrationBuilder.InsertData(
                table: "system_roles",
                columns: new[] { "id", "all_permissions", "description", "immutable", "name", "permissions" },
                values: new object[,]
                {
                    { new Guid("1da3027e-725d-4753-9455-a836ed9bdb1e"), false, "Can perform all View actions, but not make any changes.", false, "Observer", new[] { 1, 6, 8, 10, 12, 14, 16, 18 } },
                    { new Guid("d80b73c3-95d7-4468-8650-c62bbd082507"), false, "Can create and manage their own Projects.", false, "Content Developer", new[] { 0 } }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "system_roles",
                keyColumn: "id",
                keyValue: new Guid("1da3027e-725d-4753-9455-a836ed9bdb1e"));

            migrationBuilder.DeleteData(
                table: "system_roles",
                keyColumn: "id",
                keyValue: new Guid("d80b73c3-95d7-4468-8650-c62bbd082507"));

            migrationBuilder.UpdateData(
                table: "system_roles",
                keyColumn: "id",
                keyValue: new Guid("f35e8fff-f996-4cba-b303-3ba515ad8d2f"),
                column: "description",
                value: "Can perform all actions");
        }
    }
}
