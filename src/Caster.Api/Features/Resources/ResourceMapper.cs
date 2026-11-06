// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Riok.Mapperly.Abstractions;

namespace Caster.Api.Features.Resources;

/// <summary>
/// A resource of a workspace's state. A single resource is answered with its attributes; a list leaves
/// them out, as AutoMapper's runtime member exclusion did.
/// </summary>
[Mapper]
public static partial class ResourceMapper
{
    public static partial Resource ToResource(this Domain.Models.Resource source);

    public static Resource[] ToResourcesWithoutAttributes(this IEnumerable<Domain.Models.Resource> source) =>
        source.Select(x => MapWithoutAttributes(x)).ToArray();

    [UserMapping(Default = false)]
    [MapperIgnoreTarget(nameof(Resource.Attributes))]
    private static partial Resource MapWithoutAttributes(Domain.Models.Resource source);
}
