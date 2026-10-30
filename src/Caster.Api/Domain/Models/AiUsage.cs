// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Caster.Api.Domain.Models;

/// <summary>
/// One call to an AI model and what it used, so what drafting with AI cost each Project is known.
/// Every AI draft is made in a Directory, whose Project the call is recorded against.
/// </summary>
public class AiUsage
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public Guid? ProjectId { get; set; }
    public virtual Project Project { get; set; }

    /// <summary>The Directory the draft was made in. Not a foreign key, so the record outlives the Directory.</summary>
    public Guid? DirectoryId { get; set; }

    /// <summary>What the call was for: design, terraform or brief (see AiTasks)</summary>
    public string Task { get; set; }

    public string Model { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int CacheReadTokens { get; set; }
    public int CacheWriteTokens { get; set; }

    /// <summary>Why the model stopped: tool_use, refusal, max_tokens... A refused call is billed too.</summary>
    public string StopReason { get; set; }

    public Guid? CreatedById { get; set; }
    public DateTime DateCreated { get; set; }
}

public class AiUsageConfiguration : IEntityTypeConfiguration<AiUsage>
{
    public void Configure(EntityTypeBuilder<AiUsage> builder)
    {
        builder.HasIndex(u => u.ProjectId);
        // what a project cost stays known after the project is deleted
        builder
            .HasOne(u => u.Project)
            .WithMany()
            .HasForeignKey(u => u.ProjectId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
