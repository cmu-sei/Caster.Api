using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Caster.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Added_SystemRole9 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "all_permissions",
                table: "system_roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "all_permissions",
                table: "project_roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "project_roles",
                keyColumn: "id",
                keyValue: new Guid("1a3f26cd-9d99-4b98-b914-12931e786198"),
                columns: new[] { "all_permissions", "permissions" },
                values: new object[] { true, new int[0] });

            migrationBuilder.UpdateData(
                table: "project_roles",
                keyColumn: "id",
                keyValue: new Guid("39aa296e-05ba-4fb0-8d74-c92cf3354c6f"),
                columns: new[] { "all_permissions", "permissions" },
                values: new object[] { false, new[] { 0 } });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "all_permissions",
                table: "system_roles");

            migrationBuilder.DropColumn(
                name: "all_permissions",
                table: "project_roles");

            migrationBuilder.UpdateData(
                table: "project_roles",
                keyColumn: "id",
                keyValue: new Guid("1a3f26cd-9d99-4b98-b914-12931e786198"),
                column: "permissions",
                value: new[] { 0 });

            migrationBuilder.UpdateData(
                table: "project_roles",
                keyColumn: "id",
                keyValue: new Guid("39aa296e-05ba-4fb0-8d74-c92cf3354c6f"),
                column: "permissions",
                value: new[] { 1 });
        }
    }
}
