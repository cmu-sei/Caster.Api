// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Caster.Api.Data.Migrations
{
    public partial class Update_Workspace_State : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "state",
                table: "workspaces",
                type: "text",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldNullable: true,
                oldType: "bytea");

            migrationBuilder.AddColumn<string>(
                name: "state_backup",
                table: "workspaces",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "state_backup",
                table: "workspaces");

            migrationBuilder.AlterColumn<byte[]>(
                name: "state",
                table: "workspaces",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(string),
                oldNullable: true,
                oldType: "text");
        }
    }
}

