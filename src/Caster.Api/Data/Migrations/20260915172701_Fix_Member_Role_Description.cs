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
    public partial class Fix_Member_Role_Description : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "project_roles",
                keyColumn: "id",
                keyValue: new Guid("f870d8ee-7332-4f7f-8ee0-63bd07cfd7e4"),
                column: "description",
                value: "Can view, edit, and import content in the Project, but not manage Memberships");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "project_roles",
                keyColumn: "id",
                keyValue: new Guid("f870d8ee-7332-4f7f-8ee0-63bd07cfd7e4"),
                column: "description",
                value: "Has read only access to the Project");
        }
    }
}
