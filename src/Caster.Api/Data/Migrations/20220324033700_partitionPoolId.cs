using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Caster.Api.Data.Migrations
{
    public partial class partitionPoolId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_partitions_pools_pool_id",
                table: "partitions");

            migrationBuilder.AlterColumn<Guid>(
                name: "pool_id",
                table: "partitions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_partitions_pools_pool_id",
                table: "partitions",
                column: "pool_id",
                principalTable: "pools",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_partitions_pools_pool_id",
                table: "partitions");

            migrationBuilder.AlterColumn<Guid>(
                name: "pool_id",
                table: "partitions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_partitions_pools_pool_id",
                table: "partitions",
                column: "pool_id",
                principalTable: "pools",
                principalColumn: "id");
        }
    }
}
