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
    public partial class AddProjectGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "project_group_id",
                table: "projects",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "project_group_id",
                table: "hosts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "project_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    partition_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_groups", x => x.id);
                    table.ForeignKey(
                        name: "FK_project_groups_partitions_partition_id",
                        column: x => x.partition_id,
                        principalTable: "partitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "project_group_environment_sets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    project_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    environment_set_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_group_environment_sets", x => x.id);
                    table.ForeignKey(
                        name: "FK_project_group_environment_sets_environment_sets_environment~",
                        column: x => x.environment_set_id,
                        principalTable: "environment_sets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_project_group_environment_sets_project_groups_project_group~",
                        column: x => x.project_group_id,
                        principalTable: "project_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_group_inventory_connections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    project_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_name = table.Column<string>(type: "text", nullable: false),
                    normalized_connection_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_group_inventory_connections", x => x.id);
                    table.ForeignKey(
                        name: "FK_project_group_inventory_connections_project_groups_project_~",
                        column: x => x.project_group_id,
                        principalTable: "project_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_group_memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    project_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("f870d8ee-7332-4f7f-8ee0-63bd07cfd7e4"))
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_group_memberships", x => x.id);
                    table.CheckConstraint("CK_project_group_memberships_user_or_group", "(user_id IS NULL) <> (group_id IS NULL)");
                    table.ForeignKey(
                        name: "FK_project_group_memberships_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_project_group_memberships_project_groups_project_group_id",
                        column: x => x.project_group_id,
                        principalTable: "project_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_project_group_memberships_project_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "project_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_project_group_memberships_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_projects_project_group_id",
                table: "projects",
                column: "project_group_id");

            migrationBuilder.CreateIndex(
                name: "IX_hosts_project_group_id",
                table: "hosts",
                column: "project_group_id");

            migrationBuilder.AddCheckConstraint(
                name: "CK_hosts_one_owner",
                table: "hosts",
                sql: "project_id IS NULL OR project_group_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_project_group_environment_sets_environment_set_id",
                table: "project_group_environment_sets",
                column: "environment_set_id");

            migrationBuilder.CreateIndex(
                name: "IX_project_group_environment_sets_project_group_id_environment~",
                table: "project_group_environment_sets",
                columns: new[] { "project_group_id", "environment_set_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_group_inventory_connections_normalized_connection_n~",
                table: "project_group_inventory_connections",
                column: "normalized_connection_name");

            migrationBuilder.CreateIndex(
                name: "IX_project_group_inventory_connections_project_group_id_normal~",
                table: "project_group_inventory_connections",
                columns: new[] { "project_group_id", "normalized_connection_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_group_memberships_group_id",
                table: "project_group_memberships",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "IX_project_group_memberships_project_group_id_user_id_group_id",
                table: "project_group_memberships",
                columns: new[] { "project_group_id", "user_id", "group_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_group_memberships_role_id",
                table: "project_group_memberships",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "IX_project_group_memberships_user_id",
                table: "project_group_memberships",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_project_groups_name",
                table: "project_groups",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_groups_partition_id",
                table: "project_groups",
                column: "partition_id");

            migrationBuilder.AddForeignKey(
                name: "FK_hosts_project_groups_project_group_id",
                table: "hosts",
                column: "project_group_id",
                principalTable: "project_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_projects_project_groups_project_group_id",
                table: "projects",
                column: "project_group_id",
                principalTable: "project_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_hosts_project_groups_project_group_id",
                table: "hosts");

            migrationBuilder.DropForeignKey(
                name: "FK_projects_project_groups_project_group_id",
                table: "projects");

            migrationBuilder.DropTable(
                name: "project_group_environment_sets");

            migrationBuilder.DropTable(
                name: "project_group_inventory_connections");

            migrationBuilder.DropTable(
                name: "project_group_memberships");

            migrationBuilder.DropTable(
                name: "project_groups");

            migrationBuilder.DropIndex(
                name: "IX_projects_project_group_id",
                table: "projects");

            migrationBuilder.DropIndex(
                name: "IX_hosts_project_group_id",
                table: "hosts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_hosts_one_owner",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "project_group_id",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "project_group_id",
                table: "hosts");
        }
    }
}
