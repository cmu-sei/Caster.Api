// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Caster.Api.Data.Migrations
{
    public partial class Changed_Configuration_File_Content_To_String : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "content",
                table: "configuration_files",
                type: "text",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldNullable: true,
                oldType: "bytea");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte[]>(
                name: "content",
                table: "configuration_files",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(string),
                oldNullable: true,
                oldType: "text");
        }
    }
}

