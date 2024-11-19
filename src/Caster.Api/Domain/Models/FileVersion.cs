// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Caster.Api.Domain.Models
{
    public class FileVersion : IEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        public Guid FileId { get; set; }
        public virtual File File { get; set; }

        public string Name { get; set; }

        public Guid? ModifiedById { get; set; }
        public virtual User ModifiedBy { get; set; }

        public string Content { get; set; }
        public DateTime? DateSaved { get; set; }

        public string Tag { get; set; }
        public Guid? TaggedById { get; set; }
        public virtual User TaggedBy { get; set; }

        public DateTime? DateTagged { get; set; }

        public FileVersion() { }

        public FileVersion(File file)
        {
            this.FileId = file.Id;
            this.Name = file.Name;
            this.Content = file.Content;
            this.ModifiedById = file.ModifiedById;
            this.DateSaved = file.DateSaved;
        }
    }

    public class FileVersionConfiguration : IEntityTypeConfiguration<FileVersion>
    {
        public void Configure(EntityTypeBuilder<FileVersion> builder)
        {
            builder
                .HasOne(f => f.File)
                .WithMany(fh => fh.FileVersions)
                .IsRequired();
        }
    }
}

