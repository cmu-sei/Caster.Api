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
using System.Collections.Generic;
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
                    var errors = ProcessSeedDataOptions(seedDataOptions, context);

                    if (errors.Any())
                    {
                        var logger = services.GetRequiredService<ILogger<Program>>();

                        foreach (var errorString in errors)
                        {
                            logger.LogError(errorString);
                        }

                        throw new ArgumentException("Errors in SeedData. Application will restart.");
                    }
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

        private static List<string> ProcessSeedDataOptions(SeedDataOptions options, CasterContext context)
        {
            List<string> errors = new();

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
                var dbGroups = context.Groups.ToHashSet();

                foreach (var group in options.Groups)
                {
                    if (!dbGroups.Any(x => x.Name == group.Name))
                    {
                        context.Groups.Add(group);
                    }
                }

                context.SaveChanges();
            }

            // If vlans are no longer Reserved in SeedData, set them back to ReservedEditable = true and Reserved = false and vice versa
            var dbPools = context.Pools.ToHashSet();

            foreach (var dbPool in dbPools)
            {
                var pool = options?.Vlans?.Pools.FirstOrDefault(x => x.Name == dbPool.Name);
                int[] reservedVlans = [];

                if (pool != null)
                {
                    reservedVlans = pool.Reserved;
                }

                context.Vlans
                    .Where(x => x.PoolId == dbPool.Id && x.Reserved && !x.ReservedEditable && !reservedVlans.Contains(x.VlanId))
                    .ExecuteUpdate(x => x.SetProperty(y => y.Reserved, false));

                context.Vlans
                    .Where(x => x.PoolId == dbPool.Id && !x.ReservedEditable && !reservedVlans.Contains(x.VlanId))
                    .ExecuteUpdate(x => x.SetProperty(y => y.ReservedEditable, true));

                context.Vlans
                    .Where(x => x.PoolId == dbPool.Id && reservedVlans.Contains(x.VlanId))
                    .ExecuteUpdate(x => x.SetProperty(y => y.Reserved, true));

                context.Vlans
                    .Where(x => x.PoolId == dbPool.Id && reservedVlans.Contains(x.VlanId))
                    .ExecuteUpdate(x => x.SetProperty(y => y.ReservedEditable, false));
            }

            if (options.Vlans != null)
            {
                var vlanErrors = options.Vlans.Validate(dbPools);

                if (vlanErrors.Any())
                {
                    errors.AddRange(vlanErrors);
                }
                else
                {
                    foreach (var pool in options.Vlans.Pools)
                    {
                        if (!dbPools.Any(x => x.Name == pool.Name))
                        {
                            var dbPool = context.CreateVlanPool(new Pool
                            {
                                Name = pool.Name,
                                IsDefault = pool.Partitions.Any(x => x.IsDefault)
                            },
                            pool.Reserved, false, default).Result;

                            var dbVlans = context.Vlans.Where(x => x.PoolId == dbPool.Id).ToList();

                            foreach (var partition in pool.Partitions)
                            {
                                var dbPartition = new Partition
                                {
                                    IsDefault = partition.IsDefault,
                                    Name = partition.Name,
                                    PoolId = dbPool.Id,
                                    Vlans = dbVlans.Where(x => partition.Vlans.ToList().Contains(x.VlanId)).ToArray()
                                };

                                context.Partitions.Add(dbPartition);
                            }

                            context.SaveChanges();
                        }
                    }
                }
            }

            return errors;
        }
    }
}
