// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.IO;

namespace Caster.Api.Domain.Models
{
    /// <summary>
    /// The result of archiving a Project, Directory, etc into a zip, tgz, etc
    /// </summary>
    public class ArchiveResult
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public Stream Data { get; set; }
    }
}
