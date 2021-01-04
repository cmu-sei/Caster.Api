// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Caster.Api.Infrastructure.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Caster.Api.Domain.Models
{
    public class File
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        public string Name { get; set; }

        public Guid DirectoryId { get; private set;}
        public virtual Directory Directory { get; set; }

        public Guid? WorkspaceId { get; private set; }
        public virtual Workspace Workspace { get; set; }

        public Guid? ModifiedById { get; private set; }
        public virtual User ModifiedBy { get; private set; }

        public Guid? LockedById { get; private set; }
        public virtual User LockedBy { get; private set; }

        public string Content { get; set;}

        public DateTime? DateSaved { get; private set; }

        public bool IsDeleted { get; set; }

        public bool AdministrativelyLocked { get; private set; }

        public virtual ICollection<FileVersion> FileVersions { get; private set; } = new HashSet<FileVersion>();

        public string Path
        {
            get
            {
                var workspacePath = this.Workspace == null ? "" : $"__Workspaces__/{this.Workspace.Name}/";
                var directoryPath = this.Directory.GetPathNames();

                return $"{directoryPath}{workspacePath}{this.Name}";
            }
        }

        public void Tag(string tag, Guid userId, DateTime dateTagged)
        {
            var fileVersion = new FileVersion(this);
            fileVersion.Tag = tag;
            fileVersion.TaggedById = userId;
            fileVersion.DateTagged = dateTagged;

            this.FileVersions.Add(fileVersion);
        }

        public void Save(Guid userId, bool isAdmin, bool bypassLock = false)
        {
            if (!bypassLock)
            {
                if (this.AdministrativelyLocked && !isAdmin)
                    throw new FileAdminLockedException();

                this.VerifyLock(userId);
            }

            this.ModifiedById = userId;
            this.DateSaved = DateTime.UtcNow;

            this.FileVersions.Add(new FileVersion(this));
        }

        public void Delete(bool isAdmin)
        {
            if (this.AdministrativelyLocked && !isAdmin)
                throw new FileAdminLockedException();

            this.IsDeleted = true;
        }

        public void Lock(Guid userId, bool isAdmin)
        {
            if (this.AdministrativelyLocked && !isAdmin)
                throw new FileAdminLockedException();

            this.VerifyLock(userId, invalidOnUnlocked: false);
            this.LockedById = userId;
        }

        public void Unlock(Guid userId)
        {
            this.VerifyLock(userId);
            this.LockedById = null;
        }

        public void AdministrativelyLock(bool isAdmin)
        {
            if (!isAdmin)
                throw new FileInsufficientPrivilegesException();

            this.AdministrativelyLocked = true;
        }

        public void AdministrativelyUnlock(bool isAdmin)
        {
            if (!isAdmin)
                throw new FileInsufficientPrivilegesException();

            this.AdministrativelyLocked = false;
        }

        public bool CanLock(Guid userId, bool isAdmin)
        {
            if (this.LockedById.HasValue)
            {
                if (this.LockedById.Value == userId)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            if (this.AdministrativelyLocked && !isAdmin)
            {
                return false;
            }

            return true;
        }

        private void VerifyLock(Guid userId, bool invalidOnUnlocked = true)
        {
            bool verified = true;

            // If not locked
            if (invalidOnUnlocked && !this.LockedById.HasValue)
                verified = false;

            // If locked by someone else
            if (this.LockedById.HasValue && this.LockedById.Value != userId)
                verified = false;

            if (!verified)
                throw new FileConflictException();
        }
    }

    public class FileConfiguration : IEntityTypeConfiguration<File>
    {
        public void Configure(EntityTypeBuilder<File> builder)
        {
            builder
                .HasOne(w => w.Directory)
                .WithMany(d => d.Files)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            builder
                .HasOne(w => w.Workspace)
                .WithMany(d => d.Files)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasQueryFilter(f => !f.IsDeleted);
        }
    }
}
