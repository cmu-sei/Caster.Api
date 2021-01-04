// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore.Migrations;

namespace Caster.Api.Data.Migrations
{
    public partial class modules_and_versions_fix : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_version_module_module_id",
                table: "version");

            migrationBuilder.DropPrimaryKey(
                name: "PK_version",
                table: "version");

            migrationBuilder.DropPrimaryKey(
                name: "PK_module",
                table: "module");

            migrationBuilder.RenameTable(
                name: "version",
                newName: "versions");

            migrationBuilder.RenameTable(
                name: "module",
                newName: "modules");

            migrationBuilder.RenameIndex(
                name: "IX_version_module_id",
                table: "versions",
                newName: "IX_versions_module_id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_versions",
                table: "versions",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_modules",
                table: "modules",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_versions_modules_module_id",
                table: "versions",
                column: "module_id",
                principalTable: "modules",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_versions_modules_module_id",
                table: "versions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_versions",
                table: "versions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_modules",
                table: "modules");

            migrationBuilder.RenameTable(
                name: "versions",
                newName: "version");

            migrationBuilder.RenameTable(
                name: "modules",
                newName: "module");

            migrationBuilder.RenameIndex(
                name: "IX_versions_module_id",
                table: "version",
                newName: "IX_version_module_id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_version",
                table: "version",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_module",
                table: "module",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_version_module_module_id",
                table: "version",
                column: "module_id",
                principalTable: "module",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}

