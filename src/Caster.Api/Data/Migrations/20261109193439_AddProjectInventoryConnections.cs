/*
Copyright 2026 Carnegie Mellon University. All Rights Reserved.
 Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
*/

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Caster.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectInventoryConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "project_inventory_connections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_name = table.Column<string>(type: "text", nullable: false),
                    normalized_connection_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_inventory_connections", x => x.id);
                    table.ForeignKey(
                        name: "FK_project_inventory_connections_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_project_inventory_connections_normalized_connection_name",
                table: "project_inventory_connections",
                column: "normalized_connection_name");

            migrationBuilder.CreateIndex(
                name: "IX_project_inventory_connections_project_id_normalized_connect~",
                table: "project_inventory_connections",
                columns: new[] { "project_id", "normalized_connection_name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_inventory_connections");
        }
    }
}
