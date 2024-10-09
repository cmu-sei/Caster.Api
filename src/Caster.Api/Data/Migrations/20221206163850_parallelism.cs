/*
Copyright 2021 Carnegie Mellon University. All Rights Reserved. 
 Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
*/

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Caster.Api.Data.Migrations
{
    public partial class Parallelism : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "parallelism",
                table: "workspaces",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "parallelism",
                table: "directories",
                type: "integer",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "parallelism",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "parallelism",
                table: "directories");
        }
    }
}
