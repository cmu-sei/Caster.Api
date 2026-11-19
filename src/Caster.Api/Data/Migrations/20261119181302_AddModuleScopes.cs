/*
Copyright 2026 Carnegie Mellon University. All Rights Reserved.
 Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
*/

﻿using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Caster.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddModuleScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "module_scopes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    paths = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_module_scopes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "project_group_module_scopes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    project_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    module_scope_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_group_module_scopes", x => x.id);
                    table.ForeignKey(
                        name: "FK_project_group_module_scopes_module_scopes_module_scope_id",
                        column: x => x.module_scope_id,
                        principalTable: "module_scopes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_project_group_module_scopes_project_groups_project_group_id",
                        column: x => x.project_group_id,
                        principalTable: "project_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_module_scopes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    module_scope_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_module_scopes", x => x.id);
                    table.ForeignKey(
                        name: "FK_project_module_scopes_module_scopes_module_scope_id",
                        column: x => x.module_scope_id,
                        principalTable: "module_scopes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_project_module_scopes_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_module_scopes_name",
                table: "module_scopes",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_group_module_scopes_module_scope_id",
                table: "project_group_module_scopes",
                column: "module_scope_id");

            migrationBuilder.CreateIndex(
                name: "IX_project_group_module_scopes_project_group_id_module_scope_id",
                table: "project_group_module_scopes",
                columns: new[] { "project_group_id", "module_scope_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_module_scopes_module_scope_id",
                table: "project_module_scopes",
                column: "module_scope_id");

            migrationBuilder.CreateIndex(
                name: "IX_project_module_scopes_project_id_module_scope_id",
                table: "project_module_scopes",
                columns: new[] { "project_id", "module_scope_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_group_module_scopes");

            migrationBuilder.DropTable(
                name: "project_module_scopes");

            migrationBuilder.DropTable(
                name: "module_scopes");
        }
    }
}
