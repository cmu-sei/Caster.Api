// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Caster.Api.Data.Migrations
{
    public partial class TaggedBy : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "tagged_by_id",
                table: "file_versions",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_file_versions_tagged_by_id",
                table: "file_versions",
                column: "tagged_by_id");

            migrationBuilder.AddForeignKey(
                name: "FK_file_versions_users_tagged_by_id",
                table: "file_versions",
                column: "tagged_by_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_file_versions_users_tagged_by_id",
                table: "file_versions");

            migrationBuilder.DropIndex(
                name: "IX_file_versions_tagged_by_id",
                table: "file_versions");

            migrationBuilder.DropColumn(
                name: "tagged_by_id",
                table: "file_versions");
        }
    }
}

