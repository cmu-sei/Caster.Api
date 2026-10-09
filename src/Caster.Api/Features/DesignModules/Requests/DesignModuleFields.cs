// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Runtime.Serialization;

namespace Caster.Api.Features.DesignModules;

public record DesignModuleFields
{
    /// <summary>
    /// The Id of the selected Module for this DesignModule
    /// </summary>
    public Guid ModuleId { get; init; }

    /// <summary>
    /// Name of the DesignModule.
    /// </summary>
    [DataMember]
    public string Name { get; init; }

    /// <summary>
    /// Version of the selected Module to use
    /// </summary>
    [DataMember]
    public string ModuleVersion { get; init; }

    /// <summary>
    /// Values for each input
    /// </summary>
    [DataMember]
    public ModuleValue[] Values { get; init; }
}
