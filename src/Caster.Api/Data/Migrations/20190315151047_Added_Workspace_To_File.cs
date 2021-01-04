// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Caster.Api.Data.Migrations
{
    public partial class Added_Workspace_To_File : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "workspace_id",
                table: "files",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_files_workspace_id",
                table: "files",
                column: "workspace_id");

            migrationBuilder.AddForeignKey(
                name: "FK_files_workspaces_workspace_id",
                table: "files",
                column: "workspace_id",
                principalTable: "workspaces",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_files_workspaces_workspace_id",
                table: "files");

            migrationBuilder.DropIndex(
                name: "IX_files_workspace_id",
                table: "files");

            migrationBuilder.DropColumn(
                name: "workspace_id",
                table: "files");
        }
    }
}

