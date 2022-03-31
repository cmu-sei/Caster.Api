using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Caster.Api.Data.Migrations
{
    public partial class vlanPoolId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_vlans_partitions_partition_id",
                table: "vlans");

            migrationBuilder.AlterColumn<Guid>(
                name: "partition_id",
                table: "vlans",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "pool_id",
                table: "vlans",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddForeignKey(
                name: "FK_vlans_partitions_partition_id",
                table: "vlans",
                column: "partition_id",
                principalTable: "partitions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_vlans_partitions_partition_id",
                table: "vlans");

            migrationBuilder.DropColumn(
                name: "pool_id",
                table: "vlans");

            migrationBuilder.AlterColumn<Guid>(
                name: "partition_id",
                table: "vlans",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_vlans_partitions_partition_id",
                table: "vlans",
                column: "partition_id",
                principalTable: "partitions",
                principalColumn: "id");
        }
    }
}
