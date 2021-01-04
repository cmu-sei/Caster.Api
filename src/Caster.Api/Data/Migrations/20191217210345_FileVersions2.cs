// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore.Migrations;

namespace Caster.Api.Data.Migrations
{
    public partial class FileVersions2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_file_versions_users_user_id",
                table: "file_versions");

            migrationBuilder.DropForeignKey(
                name: "FK_files_users_user_id",
                table: "files");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "files",
                newName: "modified_by_id");

            migrationBuilder.RenameIndex(
                name: "IX_files_user_id",
                table: "files",
                newName: "IX_files_modified_by_id");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "file_versions",
                newName: "modified_by_id");

            migrationBuilder.RenameIndex(
                name: "IX_file_versions_user_id",
                table: "file_versions",
                newName: "IX_file_versions_modified_by_id");

            migrationBuilder.AddForeignKey(
                name: "FK_file_versions_users_modified_by_id",
                table: "file_versions",
                column: "modified_by_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_files_users_modified_by_id",
                table: "files",
                column: "modified_by_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_file_versions_users_modified_by_id",
                table: "file_versions");

            migrationBuilder.DropForeignKey(
                name: "FK_files_users_modified_by_id",
                table: "files");

            migrationBuilder.RenameColumn(
                name: "modified_by_id",
                table: "files",
                newName: "user_id");

            migrationBuilder.RenameIndex(
                name: "IX_files_modified_by_id",
                table: "files",
                newName: "IX_files_user_id");

            migrationBuilder.RenameColumn(
                name: "modified_by_id",
                table: "file_versions",
                newName: "user_id");

            migrationBuilder.RenameIndex(
                name: "IX_file_versions_modified_by_id",
                table: "file_versions",
                newName: "IX_file_versions_user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_file_versions_users_user_id",
                table: "file_versions",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_files_users_user_id",
                table: "files",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}

