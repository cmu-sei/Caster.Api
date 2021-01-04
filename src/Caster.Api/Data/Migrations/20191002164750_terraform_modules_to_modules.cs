// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore.Migrations;

namespace Caster.Api.Data.Migrations
{
    public partial class terraform_modules_to_modules : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "terraform_modules");

            migrationBuilder.CreateTable(
                name: "modules",
                columns: table => new
                {
                    id = table.Column<string>(nullable: false),
                    name = table.Column<string>(nullable: true),
                    path = table.Column<string>(nullable: true),
                    description = table.Column<string>(nullable: true),
                    url_link = table.Column<string>(nullable: true),
                    versions = table.Column<string>(nullable: true),
                    variables = table.Column<string>(nullable: true),
                    outputs = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_modules", x => x.id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "modules");

            migrationBuilder.CreateTable(
                name: "terraform_modules",
                columns: table => new
                {
                    id = table.Column<string>(nullable: false),
                    description = table.Column<string>(nullable: true),
                    name = table.Column<string>(nullable: true),
                    outputs = table.Column<string>(nullable: true),
                    path = table.Column<string>(nullable: true),
                    url_link = table.Column<string>(nullable: true),
                    variables = table.Column<string>(nullable: true),
                    versions = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_terraform_modules", x => x.id);
                });
        }
    }
}

