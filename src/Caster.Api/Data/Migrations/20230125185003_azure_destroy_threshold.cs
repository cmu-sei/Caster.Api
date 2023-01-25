using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Caster.Api.Data.Migrations
{
    public partial class azure_destroy_threshold : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "azure_destroy_failure_threshold",
                table: "workspaces",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "azure_destroy_failure_threshold",
                table: "directories",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "azure_destroy_failure_threshold_enabled",
                table: "directories",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "azure_destroy_failure_threshold",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "azure_destroy_failure_threshold",
                table: "directories");

            migrationBuilder.DropColumn(
                name: "azure_destroy_failure_threshold_enabled",
                table: "directories");
        }
    }
}
