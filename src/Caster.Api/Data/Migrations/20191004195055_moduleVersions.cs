// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Caster.Api.Data.Migrations
{
    public partial class moduleVersions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "versions");

            migrationBuilder.CreateTable(
                name: "module_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    module_id = table.Column<Guid>(nullable: false),
                    name = table.Column<string>(nullable: true),
                    url_link = table.Column<string>(nullable: true),
                    variables = table.Column<string>(nullable: true),
                    outputs = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_module_versions", x => x.id);
                    table.ForeignKey(
                        name: "FK_module_versions_modules_module_id",
                        column: x => x.module_id,
                        principalTable: "modules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_module_versions_module_id",
                table: "module_versions",
                column: "module_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "module_versions");

            migrationBuilder.CreateTable(
                name: "versions",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    module_id = table.Column<Guid>(nullable: false),
                    name = table.Column<string>(nullable: true),
                    outputs = table.Column<string>(nullable: true),
                    url_link = table.Column<string>(nullable: true),
                    variables = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_versions", x => x.id);
                    table.ForeignKey(
                        name: "FK_versions_modules_module_id",
                        column: x => x.module_id,
                        principalTable: "modules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_versions_module_id",
                table: "versions",
                column: "module_id");
        }
    }
}

