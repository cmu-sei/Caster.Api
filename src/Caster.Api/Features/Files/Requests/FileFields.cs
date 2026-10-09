// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Runtime.Serialization;
using Caster.Api.Features.Files.Interfaces;

namespace Caster.Api.Features.Files;

public abstract class FileFields : FileUpdateRequest
{
    /// <summary>
    /// Name of the file.
    /// </summary>
    [DataMember]
    public string Name { get; set; }

    /// <summary>
    /// The full contents of the file.
    /// </summary>
    [DataMember]
    public override string Content { get; set; }
}
