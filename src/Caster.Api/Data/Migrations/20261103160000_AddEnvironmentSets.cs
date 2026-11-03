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
    public partial class AddEnvironmentSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "environment_sets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_environment_sets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "environment_set_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    environment_set_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    target_name = table.Column<string>(type: "text", nullable: false),
                    value_source = table.Column<int>(type: "integer", nullable: false),
                    secret_name = table.Column<string>(type: "text", nullable: true),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_environment_set_entries", x => x.id);
                    table.ForeignKey(
                        name: "FK_environment_set_entries_environment_sets_environment_set_id",
                        column: x => x.environment_set_id,
                        principalTable: "environment_sets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_environment_sets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    environment_set_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_environment_sets", x => x.id);
                    table.ForeignKey(
                        name: "FK_project_environment_sets_environment_sets_environment_set_id",
                        column: x => x.environment_set_id,
                        principalTable: "environment_sets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_project_environment_sets_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // The seeded Observer role gets every View permission, so it gains ViewEnvironmentSets (22).
            // Appended rather than overwritten, so an Observer role an operator has changed keeps its changes.
            migrationBuilder.Sql(
                "UPDATE system_roles SET permissions = array_append(permissions, 22) " +
                "WHERE id = '1da3027e-725d-4753-9455-a836ed9bdb1e' AND NOT (22 = ANY(permissions));");

            migrationBuilder.CreateIndex(
                name: "IX_environment_set_entries_environment_set_id_kind_target_name",
                table: "environment_set_entries",
                columns: new[] { "environment_set_id", "kind", "target_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_environment_sets_name",
                table: "environment_sets",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_environment_sets_environment_set_id",
                table: "project_environment_sets",
                column: "environment_set_id");

            migrationBuilder.CreateIndex(
                name: "IX_project_environment_sets_project_id_environment_set_id",
                table: "project_environment_sets",
                columns: new[] { "project_id", "environment_set_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "environment_set_entries");

            migrationBuilder.DropTable(
                name: "project_environment_sets");

            migrationBuilder.DropTable(
                name: "environment_sets");

            // ViewEnvironmentSets (22) and ManageEnvironmentSets (23) no longer exist.
            migrationBuilder.Sql("UPDATE system_roles SET permissions = array_remove(array_remove(permissions, 22), 23);");
        }
    }
}
