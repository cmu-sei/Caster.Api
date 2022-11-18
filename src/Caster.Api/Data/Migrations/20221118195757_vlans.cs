/*
Copyright 2021 Carnegie Mellon University. All Rights Reserved. 
 Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
*/

﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Caster.Api.Data.Migrations
{
    public partial class vlans : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "partition_id",
                table: "projects",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pools",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    name = table.Column<string>(type: "text", nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pools", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "partitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    pool_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_partitions", x => x.id);
                    table.ForeignKey(
                        name: "FK_partitions_pools_pool_id",
                        column: x => x.pool_id,
                        principalTable: "pools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vlans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    pool_id = table.Column<Guid>(type: "uuid", nullable: false),
                    partition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vlan_id = table.Column<int>(type: "integer", nullable: false),
                    in_use = table.Column<bool>(type: "boolean", nullable: false),
                    tag = table.Column<string>(type: "text", nullable: true),
                    reserved = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vlans", x => x.id);
                    table.ForeignKey(
                        name: "FK_vlans_partitions_partition_id",
                        column: x => x.partition_id,
                        principalTable: "partitions",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_vlans_pools_pool_id",
                        column: x => x.pool_id,
                        principalTable: "pools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_projects_partition_id",
                table: "projects",
                column: "partition_id");

            migrationBuilder.CreateIndex(
                name: "IX_partitions_pool_id",
                table: "partitions",
                column: "pool_id");

            migrationBuilder.CreateIndex(
                name: "IX_vlans_partition_id",
                table: "vlans",
                column: "partition_id");

            migrationBuilder.CreateIndex(
                name: "IX_vlans_pool_id",
                table: "vlans",
                column: "pool_id");

            migrationBuilder.CreateIndex(
                name: "IX_vlans_vlan_id",
                table: "vlans",
                column: "vlan_id");

            migrationBuilder.AddForeignKey(
                name: "FK_projects_partitions_partition_id",
                table: "projects",
                column: "partition_id",
                principalTable: "partitions",
                principalColumn: "id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_projects_partitions_partition_id",
                table: "projects");

            migrationBuilder.DropTable(
                name: "vlans");

            migrationBuilder.DropTable(
                name: "partitions");

            migrationBuilder.DropTable(
                name: "pools");

            migrationBuilder.DropIndex(
                name: "IX_projects_partition_id",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "partition_id",
                table: "projects");
        }
    }
}
