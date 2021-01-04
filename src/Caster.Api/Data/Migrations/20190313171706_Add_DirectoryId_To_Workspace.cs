// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Caster.Api.Data.Migrations
{
    public partial class Add_DirectoryId_To_Workspace : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_workspaces_directories_directory_id",
                table: "workspaces");

            migrationBuilder.AlterColumn<Guid>(
                name: "directory_id",
                table: "workspaces",
                nullable: false,
                oldClrType: typeof(Guid),
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_workspaces_directories_directory_id",
                table: "workspaces",
                column: "directory_id",
                principalTable: "directories",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_workspaces_directories_directory_id",
                table: "workspaces");

            migrationBuilder.AlterColumn<Guid>(
                name: "directory_id",
                table: "workspaces",
                nullable: true,
                oldClrType: typeof(Guid));

            migrationBuilder.AddForeignKey(
                name: "FK_workspaces_directories_directory_id",
                table: "workspaces",
                column: "directory_id",
                principalTable: "directories",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}

