// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Caster.Api.Domain.Models;

public class GroupMembership : IEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public Guid GroupId { get; set; }
    public virtual Group Group { get; set; }

    public Guid UserId { get; set; }
    public virtual User User { get; set; }

    public GroupMembershipRole Role { get; set; } = GroupMembershipRole.Member;

    public GroupMembership() { }

    public GroupMembership(Guid groupId, Guid userId, GroupMembershipRole role = GroupMembershipRole.Member)
    {
        GroupId = groupId;
        UserId = userId;
        Role = role;
    }

    public class GroupMembershipConfiguration : IEntityTypeConfiguration<GroupMembership>
    {
        public void Configure(EntityTypeBuilder<GroupMembership> builder)
        {
            builder.HasIndex(e => new { e.GroupId, e.UserId }).IsUnique();

            builder.Property(x => x.Role).HasDefaultValue(GroupMembershipRole.Member);

            builder
                .HasOne(tu => tu.Group)
                .WithMany(t => t.Memberships)
                .HasForeignKey(tu => tu.GroupId);

            builder
                .HasOne(tu => tu.User)
                .WithMany(u => u.GroupMemberships)
                .HasForeignKey(tu => tu.UserId)
                .HasPrincipalKey(u => u.Id);
        }
    }
}

public enum GroupMembershipRole
{
    Member,
    Manager
}
