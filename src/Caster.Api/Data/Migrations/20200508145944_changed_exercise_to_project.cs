// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

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
