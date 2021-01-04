// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Features.Users;

namespace Caster.Api.Features.Files
{
    public class File
    {
        /// <summary>
        /// ID of the file.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Name of the file.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// ID of the directory this file is under.
        /// </summary>
        public Guid DirectoryId { get; set;}

        /// <summary>
        /// An optional Workspace that this File is assigned to.
        /// </summary>
        public Guid? WorkspaceId { get; set; }

        /// <summary>
        /// The full contents of the file.
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// The ID of the user who saved the file last.
        /// </summary>
        public Guid? ModifiedById { get; set; }

        /// <summary>
        /// The name of the user who saved the file last.
        /// </summary>
        public string ModifiedByName { get; set; }

        /// <summary>
        /// The date the file was saved.
        /// </summary>
        public DateTime? DateSaved { get; set; }

        /// <summary>
        /// Flag to indicate that this file has been deleted.
        /// </summary>
        public bool IsDeleted { get; set; }

        /// <summary>
        /// The Id of the User that currently has a lock on this File or null if unlocked
        /// </summary>
        public Guid? LockedById { get; set; }

        /// <summary>
        /// The name of the User that currently has a lock on this File or null if unlocked
        /// </summary>
        public string LockedByName { get; set; }

        /// <summary>
        /// Only System Admins can make changes to this file while this property is true
        /// </summary>
        public bool AdministrativelyLocked { get; private set; }
    }
}

