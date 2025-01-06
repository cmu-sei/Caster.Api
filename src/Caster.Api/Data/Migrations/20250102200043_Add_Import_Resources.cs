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
    public partial class Add_Import_Resources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "system_roles",
                keyColumn: "id",
                keyValue: new Guid("1da3027e-725d-4753-9455-a836ed9bdb1e"),
                column: "permissions",
                value: new[] { 1, 7, 9, 11, 13, 15, 17, 19 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "system_roles",
                keyColumn: "id",
                keyValue: new Guid("1da3027e-725d-4753-9455-a836ed9bdb1e"),
                column: "permissions",
                value: new[] { 1, 6, 8, 10, 12, 14, 16, 18 });
        }
    }
}
