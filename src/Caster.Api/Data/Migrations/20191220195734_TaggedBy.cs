/*
Crucible
Copyright 2020 Carnegie Mellon University.
NO WARRANTY. THIS CARNEGIE MELLON UNIVERSITY AND SOFTWARE ENGINEERING INSTITUTE MATERIAL IS FURNISHED ON AN "AS-IS" BASIS. CARNEGIE MELLON UNIVERSITY MAKES NO WARRANTIES OF ANY KIND, EITHER EXPRESSED OR IMPLIED, AS TO ANY MATTER INCLUDING, BUT NOT LIMITED TO, WARRANTY OF FITNESS FOR PURPOSE OR MERCHANTABILITY, EXCLUSIVITY, OR RESULTS OBTAINED FROM USE OF THE MATERIAL. CARNEGIE MELLON UNIVERSITY DOES NOT MAKE ANY WARRANTY OF ANY KIND WITH RESPECT TO FREEDOM FROM PATENT, TRADEMARK, OR COPYRIGHT INFRINGEMENT.
Released under a MIT (SEI)-style license, please see license.txt or contact permission@sei.cmu.edu for full terms.
[DISTRIBUTION STATEMENT A] This material has been approved for public release and unlimited distribution.  Please see Copyright notice for non-US Government use and distribution.
Carnegie Mellon(R) and CERT(R) are registered in the U.S. Patent and Trademark Office by Carnegie Mellon University.
DM20-0181
*/

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

