using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Caster.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Added_SystemRole8 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_project_memberships_users_user_id",
                table: "project_memberships");

            migrationBuilder.DropIndex(
                name: "IX_project_memberships_project_id_user_id",
                table: "project_memberships");

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "project_memberships",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "group_id",
                table: "project_memberships",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_memberships_group_id",
                table: "project_memberships",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "IX_project_memberships_project_id_user_id_group_id",
                table: "project_memberships",
                columns: new[] { "project_id", "user_id", "group_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_project_memberships_groups_group_id",
                table: "project_memberships",
                column: "group_id",
                principalTable: "groups",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_project_memberships_users_user_id",
                table: "project_memberships",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_project_memberships_groups_group_id",
                table: "project_memberships");

            migrationBuilder.DropForeignKey(
                name: "FK_project_memberships_users_user_id",
                table: "project_memberships");

            migrationBuilder.DropIndex(
                name: "IX_project_memberships_group_id",
                table: "project_memberships");

            migrationBuilder.DropIndex(
                name: "IX_project_memberships_project_id_user_id_group_id",
                table: "project_memberships");

            migrationBuilder.DropColumn(
                name: "group_id",
                table: "project_memberships");

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "project_memberships",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_memberships_project_id_user_id",
                table: "project_memberships",
                columns: new[] { "project_id", "user_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_project_memberships_users_user_id",
                table: "project_memberships",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
