/*
Copyright 2026 Carnegie Mellon University. All Rights Reserved.
 Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
*/

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Caster.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEnvironmentSetConnectionSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "connection_field",
                table: "environment_set_entries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "connection_name",
                table: "environment_set_entries",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The older schema has no Connection value source (2).
            migrationBuilder.Sql("DELETE FROM environment_set_entries WHERE value_source = 2;");

            migrationBuilder.DropColumn(
                name: "connection_field",
                table: "environment_set_entries");

            migrationBuilder.DropColumn(
                name: "connection_name",
                table: "environment_set_entries");

            // ManageInfrastructure (24), added with connection entries, no longer exists.
            migrationBuilder.Sql("UPDATE system_roles SET permissions = array_remove(permissions, 24);");
        }
    }
}
