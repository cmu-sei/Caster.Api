// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Caster.Api.Domain.Models
{
    public class Run
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        public virtual Plan Plan { get; set; }
        public virtual Apply Apply { get; set; }

        public Guid WorkspaceId { get; set; }
        public virtual Workspace Workspace { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDestroy { get; set; }
        public RunStatus Status { get; set; } = RunStatus.Queued;

        public string[] Targets { get; set; }
    }

    public enum RunStatus
    {
        Queued = 0,
        Failed = 1,
        Rejected = 2,
        Planning = 3,
        Planned = 4,
        Applying = 5,
        Applied = 6,

        [EnumMember(Value = "Applied - State Error")]
        Applied_StateError = 7,

        [EnumMember(Value = "Failed - State Error")]
        Failed_StateError = 8
    }

    public static class RunHelpers
    {
        public static RunStatus[] GetActiveStatuses()
        {
            return new RunStatus[]
            {
                RunStatus.Queued,
                RunStatus.Planning,
                RunStatus.Applying,
            };
        }
    }

    public class RunConfiguration : IEntityTypeConfiguration<Run>
    {
        public void Configure(EntityTypeBuilder<Run> builder)
        {
            builder
                .Property<string[]>(r => r.Targets)
                .HasConversion(
                    list => String.Join('\n', list),
                    str => str.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                );

            builder
                .HasOne(r => r.Plan)
                .WithOne(p => p.Run)
                .HasForeignKey<Plan>(p => p.RunId);

            builder
                .HasOne(r => r.Apply)
                .WithOne(a => a.Run)
                .HasForeignKey<Apply>(a => a.RunId);

            builder.HasIndex(r => r.CreatedAt);
        }
    }
}
