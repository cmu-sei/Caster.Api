// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Caster.Api.Domain.Models
{
    public class UserPermission
    {
        public UserPermission() { }

        public UserPermission(Guid userId, Guid permissionId)
        {
            UserId = userId;
            PermissionId = permissionId;
        }

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        public Guid UserId { get; set; }
        public User User { get; set; }

        public Guid PermissionId { get; set; }
        public Permission Permission { get; set; }
    }

    public class UserPermissionConfiguration : IEntityTypeConfiguration<UserPermission>
    {
        public void Configure(EntityTypeBuilder<UserPermission> builder)
        {
            builder.HasIndex(x => new { x.UserId, x.PermissionId }).IsUnique();

            builder
                .HasOne(u => u.User)
                .WithMany(p => p.UserPermissions)
                .HasForeignKey(x => x.UserId);
            builder
                .HasOne(u => u.Permission)
                .WithMany(p => p.UserPermissions)
                .HasForeignKey(x => x.PermissionId);
        }
    }
}

