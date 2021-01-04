// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;

namespace Caster.Api.Infrastructure.Extensions
{
    public static class DatabaseExtensions
    {
        public static IWebHost InitializeDatabase(this IWebHost webHost)
        {
            using (var serviceScope = webHost.Services.CreateScope())
            {
                var services = serviceScope.ServiceProvider;

                try
                {
                    var context = serviceScope.ServiceProvider.GetRequiredService<CasterContext>();
                    context.Database.Migrate();

                    var seedDataOptions = services.GetService<SeedDataOptions>();
                    ProcessSeedDataOptions(seedDataOptions, context);
                }
                catch (Exception ex)
                {
                    var logger = services.GetRequiredService<ILogger<Program>>();
                    logger.LogError(ex, "An error occurred while initializing the database.");

                    // exit on database connection error on startup so app can be restarted to try again
                    throw;
                }

            }

            return webHost;
        }

        private static void ProcessSeedDataOptions(SeedDataOptions options, CasterContext context)
        {
            if (options.Permissions.Any())
            {
                var dbPermissions = context.Permissions.ToList();

                foreach (Permission permission in options.Permissions)
                {
                    if (!dbPermissions.Where(x => x.Key == permission.Key && x.Value == permission.Value).Any())
                    {
                        context.Permissions.Add(permission);
                    }
                }

                context.SaveChanges();
            }
            if (options.Users.Any())
            {
                var dbUsers = context.Users.ToList();

                foreach (User user in options.Users)
                {
                    if (!dbUsers.Where(x => x.Id == user.Id).Any())
                    {
                        context.Users.Add(user);
                    }
                }

                context.SaveChanges();
            }
            if (options.UserPermissions.Any())
            {
                var dbUserPermissions = context.UserPermissions.ToList();

                foreach (UserPermission userPermission in options.UserPermissions)
                {
                    if (!dbUserPermissions.Where(x => x.UserId == userPermission.UserId && x.PermissionId == userPermission.PermissionId).Any())
                    {
                        context.UserPermissions.Add(userPermission);
                    }
                }

                context.SaveChanges();
            }
        }


    }
}
