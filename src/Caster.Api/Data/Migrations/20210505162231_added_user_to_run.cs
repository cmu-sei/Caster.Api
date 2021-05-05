using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Caster.Api.Data.Migrations
{
    public partial class added_user_to_run : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "created_by_id",
                table: "runs",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "modified_at",
                table: "runs",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "modified_by_id",
                table: "runs",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_runs_created_by_id",
                table: "runs",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_runs_modified_by_id",
                table: "runs",
                column: "modified_by_id");

            migrationBuilder.AddForeignKey(
                name: "FK_runs_users_created_by_id",
                table: "runs",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_runs_users_modified_by_id",
                table: "runs",
                column: "modified_by_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_runs_users_created_by_id",
                table: "runs");

            migrationBuilder.DropForeignKey(
                name: "FK_runs_users_modified_by_id",
                table: "runs");

            migrationBuilder.DropIndex(
                name: "IX_runs_created_by_id",
                table: "runs");

            migrationBuilder.DropIndex(
                name: "IX_runs_modified_by_id",
                table: "runs");

            migrationBuilder.DropColumn(
                name: "created_by_id",
                table: "runs");

            migrationBuilder.DropColumn(
                name: "modified_at",
                table: "runs");

            migrationBuilder.DropColumn(
                name: "modified_by_id",
                table: "runs");
        }
    }
}
