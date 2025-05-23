/*
Copyright 2021 Carnegie Mellon University. All Rights Reserved. 
 Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
*/

﻿using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Caster.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Migrate_Old_Permissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE users
                SET role_id = (
                    SELECT id
                    FROM system_roles
                    WHERE name = 'Content Developer'
                )
                WHERE id IN (
                    SELECT user_id
                    FROM user_permissions
                    WHERE permission_id = (
                        SELECT id
                        FROM permissions
                        WHERE key = 'ContentDeveloper'
                    )
                )
            ");

            migrationBuilder.Sql(@"
                UPDATE users
                SET role_id = (
                    SELECT id
                    FROM system_roles
                    WHERE name = 'Administrator'
                )
                WHERE id IN (
                    SELECT user_id
                    FROM user_permissions
                    WHERE permission_id = (
                        SELECT id
                        FROM permissions
                        WHERE key = 'SystemAdmin'
                    )
                )
            ");

            migrationBuilder.Sql(@"
                INSERT INTO project_memberships (user_id, project_id)
                SELECT
                    u.id,
                    p.id
                FROM
                    users u
                INNER JOIN
                    user_permissions up ON u.id = up.user_id
                CROSS JOIN
                    projects p;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"UPDATE users SET role_id = null");
        }
    }
}
