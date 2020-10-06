/*
Crucible
Copyright 2020 Carnegie Mellon University.
NO WARRANTY. THIS CARNEGIE MELLON UNIVERSITY AND SOFTWARE ENGINEERING INSTITUTE MATERIAL IS FURNISHED ON AN "AS-IS" BASIS. CARNEGIE MELLON UNIVERSITY MAKES NO WARRANTIES OF ANY KIND, EITHER EXPRESSED OR IMPLIED, AS TO ANY MATTER INCLUDING, BUT NOT LIMITED TO, WARRANTY OF FITNESS FOR PURPOSE OR MERCHANTABILITY, EXCLUSIVITY, OR RESULTS OBTAINED FROM USE OF THE MATERIAL. CARNEGIE MELLON UNIVERSITY DOES NOT MAKE ANY WARRANTY OF ANY KIND WITH RESPECT TO FREEDOM FROM PATENT, TRADEMARK, OR COPYRIGHT INFRINGEMENT.
Released under a MIT (SEI)-style license, please see license.txt or contact permission@sei.cmu.edu for full terms.
[DISTRIBUTION STATEMENT A] This material has been approved for public release and unlimited distribution.  Please see Copyright notice for non-US Government use and distribution.
Carnegie Mellon(R) and CERT(R) are registered in the U.S. Patent and Trademark Office by Carnegie Mellon University.
DM20-0181
*/

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

