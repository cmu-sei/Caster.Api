// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Data;

public partial class CasterContext : DbContext
{
    public List<Entry> Entries { get; set; } = new List<Entry>();

    private DbContextOptions<CasterContext> _options;

    public CasterContext(DbContextOptions<CasterContext> options) : base(options)
    {
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
    public DbSet<Design> Designs { get; set; }
    public DbSet<DesignModule> DesignModules { get; set; }
    public DbSet<Variable> Variables { get; set; }

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

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        SaveEntries();
        return await base.SaveChangesAsync(ct);
    }

    /// <summary>
    /// keep track of changes across multiple savechanges in a transaction, without duplicates
    /// </summary>
    private void SaveEntries()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            // find value of id property
            var id = entry.Properties
                .FirstOrDefault(x =>
                    x.Metadata.ValueGenerated == Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAdd)?.CurrentValue;

            // find matching existing entry, if any
            var e = Entries.FirstOrDefault(x => x.Properties.FirstOrDefault(y =>
                y.Metadata.ValueGenerated == Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAdd)?.CurrentValue == id);

            if (e != null)
            {
                // if entry already exists, mark which properties were previously modified,
                // remove old entry and add new one, to avoid duplicates
                var modifiedProperties = e.Properties
                    .Where(x => x.IsModified)
                    .Select(x => x.Metadata.Name)
                    .ToArray();

                var newEntry = new Entry(entry);

                foreach (var property in newEntry.Properties)
                {
                    if (modifiedProperties.Contains(property.Metadata.Name))
                    {
                        property.IsModified = true;
                    }
                }

                Entries.Remove(e);
                Entries.Add(newEntry);
            }
            else
            {
                Entries.Add(new Entry(entry));
            }
        }
    }
}