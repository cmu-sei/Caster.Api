using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Caster.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Added_SystemRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "role_id",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "system_role",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    name = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    permissions = table.Column<int[]>(type: "integer[]", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_role", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_users_role_id",
                table: "users",
                column: "role_id");

            migrationBuilder.AddForeignKey(
                name: "FK_users_system_role_role_id",
                table: "users",
                column: "role_id",
                principalTable: "system_role",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_users_system_role_role_id",
                table: "users");

            migrationBuilder.DropTable(
                name: "system_role");

            migrationBuilder.DropIndex(
                name: "IX_users_role_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "role_id",
                table: "users");
        }
    }
}
