// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Data
{
    public partial class CasterContext : DbContext
    {
        private DbContextOptions<CasterContext> _options;

        public CasterContext(DbContextOptions<CasterContext> options) : base(options) {
            _options = options;
        }

        public DbSet<Project> Projects { get; set; }
        public DbSet<Directory> Directories { get; set; }
        public DbSet<File> Files { get; set; }
        public DbSet<FileVersion> FileVersions { get; set; }
        public DbSet<Workspace> Workspaces { get; set; }
        public DbSet<Run> Runs { get; set; }
        public DbSet<Plan> Plans { get; set; }
        public DbSet<Apply> Applies { get; set; }
        public DbSet<RemovedResource> RemovedResources { get; set; }
        public DbSet<Module> Modules { get; set; }
        public DbSet<ModuleVersion> ModuleVersions { get; set; }
        public DbSet<Permission> Permissions { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<UserPermission> UserPermissions { get; set; }
        public DbSet<Host> Hosts { get; set; }
        public DbSet<HostMachine> HostMachines { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurations();

            // Apply PostgreSQL specific options
            if (Database.IsNpgsql())
            {
                modelBuilder.AddPostgresUUIDGeneration();
                modelBuilder.UsePostgresCasing();
            }
        }
    }
}
