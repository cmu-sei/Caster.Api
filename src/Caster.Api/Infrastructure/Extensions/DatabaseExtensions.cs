// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;

namespace Caster.Api.Infrastructure.Extensions
{
    public static class DatabaseExtensions
    {
        public static IHost InitializeDatabase(this IHost webHost)
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
            if (options.Roles?.Any() == true)
            {
                var dbRoles = context.SystemRoles.ToHashSet();

                foreach (var role in options.Roles)
                {
                    if (!dbRoles.Any(x => x.Name == role.Name))
                    {
                        context.SystemRoles.Add(role);
                    }
                }

                context.SaveChanges();
            }

            if (options.Users?.Any() == true)
            {
                var dbUserIds = context.Users.Select(x => x.Id).ToHashSet();

                foreach (User user in options.Users)
                {
                    if (!dbUserIds.Contains(user.Id))
                    {
                        if (user.Role?.Id == Guid.Empty && !string.IsNullOrEmpty(user.Role.Name))
                        {
                            var role = context.SystemRoles.FirstOrDefault(x => x.Name == user.Role.Name);
                            if (role != null)
                            {
                                user.RoleId = role.Id;
                                user.Role = role;
                            }
                            else
                            {
                                user.RoleId = null;
                                user.Role = null;
                            }
                        }

                        context.Users.Add(user);
                    }
                }

                context.SaveChanges();
            }

            if (options.Groups?.Any() == true)
            {
                var dbGroup = context.Groups.ToHashSet();

                foreach (var group in options.Groups)
                {
                    if (!dbGroup.Any(x => x.Name == group.Name))
                    {
                        context.Groups.Add(group);
                    }
                }

                context.SaveChanges();
            }
        }
    }
}
