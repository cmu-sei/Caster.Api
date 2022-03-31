using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Caster.Api.Data.Migrations
{
    public partial class vlan : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pools",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    name = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pools", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "partitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    name = table.Column<string>(type: "text", nullable: true),
                    pool_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_partitions", x => x.id);
                    table.ForeignKey(
                        name: "FK_partitions_pools_pool_id",
                        column: x => x.pool_id,
                        principalTable: "pools",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "vlans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    vlan = table.Column<int>(type: "integer", nullable: false),
                    in_use = table.Column<bool>(type: "boolean", nullable: false),
                    partition_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vlans", x => x.id);
                    table.ForeignKey(
                        name: "FK_vlans_partitions_partition_id",
                        column: x => x.partition_id,
                        principalTable: "partitions",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_partitions_pool_id",
                table: "partitions",
                column: "pool_id");

            migrationBuilder.CreateIndex(
                name: "IX_vlans_partition_id",
                table: "vlans",
                column: "partition_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vlans");

            migrationBuilder.DropTable(
                name: "partitions");

            migrationBuilder.DropTable(
                name: "pools");
        }
    }
}
