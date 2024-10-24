using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Caster.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Added_SystemRole5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "project_roles",
                columns: new[] { "id", "description", "name", "permissions" },
                values: new object[,]
                {
                    { new Guid("1a3f26cd-9d99-4b98-b914-12931e786198"), "Can perform all actions on the Project", "Administrator", new[] { 0 } },
                    { new Guid("39aa296e-05ba-4fb0-8d74-c92cf3354c6f"), "Has read only access to the Project", "Observer", new[] { 1 } }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "project_roles",
                keyColumn: "id",
                keyValue: new Guid("1a3f26cd-9d99-4b98-b914-12931e786198"));

            migrationBuilder.DeleteData(
                table: "project_roles",
                keyColumn: "id",
                keyValue: new Guid("39aa296e-05ba-4fb0-8d74-c92cf3354c6f"));
        }
    }
}
