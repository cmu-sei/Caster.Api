/*
Copyright 2021 Carnegie Mellon University. All Rights Reserved. 
 Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
*/

﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Caster.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddModuleSourceNameAndMissingSince : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "missing_since",
                table: "modules",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_name",
                table: "modules",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_modules_source_name",
                table: "modules",
                column: "source_name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_modules_source_name",
                table: "modules");

            migrationBuilder.DropColumn(
                name: "missing_since",
                table: "modules");

            migrationBuilder.DropColumn(
                name: "source_name",
                table: "modules");
        }
    }
}
