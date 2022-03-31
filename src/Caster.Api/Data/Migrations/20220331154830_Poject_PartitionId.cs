using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Caster.Api.Data.Migrations
{
    public partial class Poject_PartitionId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "project_id",
                table: "partitions");

            migrationBuilder.AddColumn<Guid>(
                name: "partition_id",
                table: "projects",
                type: "uuid",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "partition_id",
                table: "projects");

            migrationBuilder.AddColumn<Guid>(
                name: "project_id",
                table: "partitions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
        }
    }
}
