using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Caster.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Added_SystemRole2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_users_system_role_role_id",
                table: "users");

            migrationBuilder.DropPrimaryKey(
                name: "PK_system_role",
                table: "system_role");

            migrationBuilder.RenameTable(
                name: "system_role",
                newName: "system_roles");

            migrationBuilder.AddColumn<bool>(
                name: "all_permissions",
                table: "system_roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddPrimaryKey(
                name: "PK_system_roles",
                table: "system_roles",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_users_system_roles_role_id",
                table: "users",
                column: "role_id",
                principalTable: "system_roles",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_users_system_roles_role_id",
                table: "users");

            migrationBuilder.DropPrimaryKey(
                name: "PK_system_roles",
                table: "system_roles");

            migrationBuilder.DropColumn(
                name: "all_permissions",
                table: "system_roles");

            migrationBuilder.RenameTable(
                name: "system_roles",
                newName: "system_role");

            migrationBuilder.AddPrimaryKey(
                name: "PK_system_role",
                table: "system_role",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_users_system_role_role_id",
                table: "users",
                column: "role_id",
                principalTable: "system_role",
                principalColumn: "id");
        }
    }
}
