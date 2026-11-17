// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using Riok.Mapperly.Abstractions;

namespace Caster.Api.Features.Variables;

[Mapper]
public static partial class VariableMapper
{
    public static partial IQueryable<Variable> ProjectToVariable(this IQueryable<Domain.Models.Variable> query);

    public static partial Variable ToVariable(this Domain.Models.Variable source);

    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    public static partial Domain.Models.Variable ToEntity(this Create.Command source);

    /// <summary>The PUT update: every member is written, so a null default value clears it.</summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(Edit.Command.Id))]
    public static partial void ApplyTo(this Edit.Command source, Domain.Models.Variable target);
}
