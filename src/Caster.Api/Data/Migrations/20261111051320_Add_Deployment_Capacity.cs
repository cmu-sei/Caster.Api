/*
Copyright 2026 Carnegie Mellon University. All Rights Reserved.
 Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
*/

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Caster.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Add_Deployment_Capacity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "max_disk_gib",
                table: "workspaces",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_machines",
                table: "workspaces",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_memory_gib",
                table: "workspaces",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_vcpu",
                table: "workspaces",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_disk_gib",
                table: "projects",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_machines",
                table: "projects",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_memory_gib",
                table: "projects",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_vcpu",
                table: "projects",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "capacity_demand",
                table: "plans",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "max_disk_gib",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "max_machines",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "max_memory_gib",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "max_vcpu",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "max_disk_gib",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "max_machines",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "max_memory_gib",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "max_vcpu",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "capacity_demand",
                table: "plans");
        }
    }
}
