// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Extensions;
using Crucible.Common.EntityEvents;
using Crucible.Common.EntityEvents.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Caster.Api.Data;

[GenerateEntityEventInterfaces(typeof(INotification))]
public partial class CasterContext : EventPublishingDbContext
{

    public CasterContext(DbContextOptions options) : base(options)
    {
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
    public DbSet<User> Users { get; set; }
    public DbSet<Host> Hosts { get; set; }
    public DbSet<HostMachine> HostMachines { get; set; }
    public DbSet<Design> Designs { get; set; }
    public DbSet<DesignModule> DesignModules { get; set; }
    public DbSet<Variable> Variables { get; set; }
    public DbSet<Vlan> Vlans { get; set; }
    public DbSet<Partition> Partitions { get; set; }
    public DbSet<Pool> Pools { get; set; }
    public DbSet<SystemRole> SystemRoles { get; set; }
    public DbSet<ProjectRole> ProjectRoles { get; set; }
    public DbSet<ProjectMembership> ProjectMemberships { get; set; }
    public DbSet<Group> Groups { get; set; }
    public DbSet<GroupMembership> GroupMemberships { get; set; }
    public DbSet<ContainerImage> ContainerImages { get; set; }

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

    public override async Task PublishEventsAsync(IReadOnlyList<IEntityEvent> events, CancellationToken cancellationToken)
    {
        if (ServiceProvider is not null)
        {
            var mediator = ServiceProvider.GetRequiredService<IMediator>();
            var logger = ServiceProvider.GetRequiredService<ILogger<CasterContext>>();

            foreach (var evt in events.Cast<INotification>())
            {
                try
                {
                    await mediator.Publish(evt, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error publishing entity event {EventType}", evt.GetType().Name);
                }
            }
        }
    }
}
