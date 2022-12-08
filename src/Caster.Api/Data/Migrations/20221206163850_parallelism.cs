using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Caster.Api.Data.Migrations
{
    public partial class parallelism : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "parallelism",
                table: "workspaces",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "parallelism",
                table: "directories",
                type: "integer",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "parallelism",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "parallelism",
                table: "directories");
        }
    }
}
