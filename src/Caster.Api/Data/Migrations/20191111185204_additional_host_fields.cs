// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore.Migrations;

namespace Caster.Api.Data.Migrations
{
    public partial class additional_host_fields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "dynamic_hosts",
                table: "exercises");

            migrationBuilder.AddColumn<bool>(
                name: "dynamic_host",
                table: "workspaces",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "development",
                table: "hosts",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "enabled",
                table: "hosts",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "dynamic_host",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "development",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "enabled",
                table: "hosts");

            migrationBuilder.AddColumn<bool>(
                name: "dynamic_hosts",
                table: "exercises",
                nullable: false,
                defaultValue: false);
        }
    }
}

