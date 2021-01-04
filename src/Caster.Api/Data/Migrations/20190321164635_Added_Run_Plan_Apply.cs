// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Caster.Api.Data.Migrations
{
    public partial class Added_Run_Plan_Apply : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "applies",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    run_id = table.Column<Guid>(nullable: false),
                    status = table.Column<int>(nullable: false),
                    output = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_applies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plans",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    run_id = table.Column<Guid>(nullable: false),
                    status = table.Column<int>(nullable: false),
                    output = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "runs",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    plan_id = table.Column<Guid>(nullable: true),
                    apply_id = table.Column<Guid>(nullable: true),
                    workspace_id = table.Column<Guid>(nullable: false),
                    created_at = table.Column<DateTime>(nullable: false),
                    is_destroy = table.Column<bool>(nullable: false),
                    status = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_runs", x => x.id);
                    table.ForeignKey(
                        name: "FK_runs_applies_apply_id",
                        column: x => x.apply_id,
                        principalTable: "applies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_runs_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_runs_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_runs_apply_id",
                table: "runs",
                column: "apply_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_runs_plan_id",
                table: "runs",
                column: "plan_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_runs_workspace_id",
                table: "runs",
                column: "workspace_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "runs");

            migrationBuilder.DropTable(
                name: "applies");

            migrationBuilder.DropTable(
                name: "plans");
        }
    }
}

