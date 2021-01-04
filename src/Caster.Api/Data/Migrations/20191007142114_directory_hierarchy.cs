// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Caster.Api.Data.Migrations
{
    public partial class directory_hierarchy : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_files_workspaces_workspace_id",
                table: "files");

            migrationBuilder.DropColumn(
                name: "shared",
                table: "directories");

            migrationBuilder.AddColumn<Guid>(
                name: "parent_id",
                table: "directories",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "path",
                table: "directories",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_directories_parent_id",
                table: "directories",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "IX_directories_path",
                table: "directories",
                column: "path");

            migrationBuilder.AddForeignKey(
                name: "FK_directories_directories_parent_id",
                table: "directories",
                column: "parent_id",
                principalTable: "directories",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_files_workspaces_workspace_id",
                table: "files",
                column: "workspace_id",
                principalTable: "workspaces",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_directories_directories_parent_id",
                table: "directories");

            migrationBuilder.DropForeignKey(
                name: "FK_files_workspaces_workspace_id",
                table: "files");

            migrationBuilder.DropIndex(
                name: "IX_directories_parent_id",
                table: "directories");

            migrationBuilder.DropIndex(
                name: "IX_directories_path",
                table: "directories");

            migrationBuilder.DropColumn(
                name: "parent_id",
                table: "directories");

            migrationBuilder.DropColumn(
                name: "path",
                table: "directories");

            migrationBuilder.AddColumn<bool>(
                name: "shared",
                table: "directories",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddForeignKey(
                name: "FK_files_workspaces_workspace_id",
                table: "files",
                column: "workspace_id",
                principalTable: "workspaces",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}

