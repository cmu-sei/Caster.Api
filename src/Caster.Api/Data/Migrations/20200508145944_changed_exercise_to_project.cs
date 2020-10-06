﻿/*
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
    public partial class changed_exercise_to_project : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_directories_exercises_exercise_id",
                table: "directories");

            migrationBuilder.DropForeignKey(
                name: "FK_hosts_exercises_exercise_id",
                table: "hosts");

            migrationBuilder.RenameTable(
                name: "exercises",
                newName: "projects");

            migrationBuilder.DropIndex(
                name: "IX_hosts_exercise_id",
                table: "hosts");

            migrationBuilder.DropIndex(
                name: "IX_directories_exercise_id",
                table: "directories");

            migrationBuilder.RenameColumn(
                name: "exercise_id",
                table: "hosts",
                newName: "project_id");

            migrationBuilder.RenameColumn(
                name: "exercise_id",
                table: "directories",
                newName: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_hosts_project_id",
                table: "hosts",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_directories_project_id",
                table: "directories",
                column: "project_id");

            migrationBuilder.AddForeignKey(
                name: "FK_directories_projects_project_id",
                table: "directories",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_hosts_projects_project_id",
                table: "hosts",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_directories_projects_project_id",
                table: "directories");

            migrationBuilder.DropForeignKey(
                name: "FK_hosts_projects_project_id",
                table: "hosts");

            migrationBuilder.RenameTable(
                name: "projects",
                newName: "exercises");

            migrationBuilder.DropIndex(
                name: "IX_hosts_project_id",
                table: "hosts");

            migrationBuilder.DropIndex(
                name: "IX_directories_project_id",
                table: "directories");

            migrationBuilder.RenameColumn(
                name: "project_id",
                table: "hosts",
                newName: "exercise_id");

            migrationBuilder.RenameColumn(
                name: "project_id",
                table: "directories",
                newName: "exercise_id");

            migrationBuilder.CreateIndex(
                name: "IX_hosts_exercise_id",
                table: "hosts",
                column: "exercise_id");

            migrationBuilder.CreateIndex(
                name: "IX_directories_exercise_id",
                table: "directories",
                column: "exercise_id");

            migrationBuilder.AddForeignKey(
                name: "FK_directories_exercises_exercise_id",
                table: "directories",
                column: "exercise_id",
                principalTable: "exercises",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_hosts_exercises_exercise_id",
                table: "hosts",
                column: "exercise_id",
                principalTable: "exercises",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
