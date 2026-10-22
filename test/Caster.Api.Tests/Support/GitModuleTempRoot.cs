// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Caster.Api.Tests.Support;

/// <summary>
/// Serializes the test classes that clone into
/// <see cref="Caster.Api.Domain.Services.Modules.GitModuleRepositoryProvider.TempDirectoryName"/>: the
/// end-to-end hygiene tests count that shared directory's children, so no other clone may run beside them.
/// </summary>
[CollectionDefinition(Name)]
public class GitModuleTempRoot
{
    public const string Name = "Git module temp root";
}
