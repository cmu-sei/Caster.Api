/*
Copyright 2026 Carnegie Mellon University. All Rights Reserved.
 Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
*/

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Caster.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Add_View_Infrastructure_Permission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Append rather than overwrite, so an Observer role an admin has changed keeps its permissions.
            migrationBuilder.Sql(
                "UPDATE system_roles SET permissions = array_append(permissions, 21) " +
                "WHERE id = '1da3027e-725d-4753-9455-a836ed9bdb1e' AND NOT (21 = ANY(permissions));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ViewInfrastructure no longer exists after rolling back, so no role keeps it.
            migrationBuilder.Sql("UPDATE system_roles SET permissions = array_remove(permissions, 21);");
        }
    }
}
