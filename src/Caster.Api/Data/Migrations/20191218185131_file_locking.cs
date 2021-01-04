// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Caster.Api.Data.Migrations
{
    public partial class file_locking : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "locked_by_id",
                table: "files",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_files_locked_by_id",
                table: "files",
                column: "locked_by_id");

            migrationBuilder.AddForeignKey(
                name: "FK_files_users_locked_by_id",
                table: "files",
                column: "locked_by_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_files_users_locked_by_id",
                table: "files");

            migrationBuilder.DropIndex(
                name: "IX_files_locked_by_id",
                table: "files");

            migrationBuilder.DropColumn(
                name: "locked_by_id",
                table: "files");
        }
    }
}

