// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Features.Users;

namespace Caster.Api.Features.Files
{
    public class FileVersion
    {
        /// <summary>
        /// ID of the file version.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// ID of the file.
        /// </summary>
        public Guid? FileId { get; set; }

        /// <summary>
        /// Name of the file.
        /// </summary>
        public string Name { get; set; }

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
        /// The full contents of the file.
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// Tag string applied to this version of the file.
        /// </summary>
        public string Tag { get; set; }

        /// <summary>
        /// The ID of the user who tagged the file.
        /// </summary>
        public Guid? TaggedById { get; set; }

        /// <summary>
        /// The name of the user who tagged the file.
        /// </summary>
        public string TaggedByName { get; set; }

        /// <summary>
        /// Date the tag was applied to this version of the file.
        /// </summary>
        public DateTime? DateTagged { get; set; }
    }
}

