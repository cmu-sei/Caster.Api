using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Caster.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Added_SystemRole12 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_project_memberships_project_roles_role_id",
                table: "project_memberships");

            migrationBuilder.AlterColumn<Guid>(
                name: "role_id",
                table: "project_memberships",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("f870d8ee-7332-4f7f-8ee0-63bd07cfd7e4"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "project_roles",
                keyColumn: "id",
                keyValue: new Guid("1a3f26cd-9d99-4b98-b914-12931e786198"),
                column: "name",
                value: "Manager");

            migrationBuilder.InsertData(
                table: "project_roles",
                columns: new[] { "id", "all_permissions", "description", "name", "permissions" },
                values: new object[] { new Guid("f870d8ee-7332-4f7f-8ee0-63bd07cfd7e4"), false, "Has read only access to the Project", "Member", new[] { 0, 1, 3 } });

            migrationBuilder.AddForeignKey(
                name: "FK_project_memberships_project_roles_role_id",
                table: "project_memberships",
                column: "role_id",
                principalTable: "project_roles",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_project_memberships_project_roles_role_id",
                table: "project_memberships");

            migrationBuilder.DeleteData(
                table: "project_roles",
                keyColumn: "id",
                keyValue: new Guid("f870d8ee-7332-4f7f-8ee0-63bd07cfd7e4"));

            migrationBuilder.AlterColumn<Guid>(
                name: "role_id",
                table: "project_memberships",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldDefaultValue: new Guid("f870d8ee-7332-4f7f-8ee0-63bd07cfd7e4"));

            migrationBuilder.UpdateData(
                table: "project_roles",
                keyColumn: "id",
                keyValue: new Guid("1a3f26cd-9d99-4b98-b914-12931e786198"),
                column: "name",
                value: "Administrator");

            migrationBuilder.AddForeignKey(
                name: "FK_project_memberships_project_roles_role_id",
                table: "project_memberships",
                column: "role_id",
                principalTable: "project_roles",
                principalColumn: "id");
        }
    }
}
