// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Xunit;

namespace Caster.Api.Tests.Unit.Modules
{
    /// <summary>
    /// Serializes the test classes that clone into
    /// <see cref="Caster.Api.Domain.Services.Modules.GitModuleRepositoryProvider.TempDirectoryName"/>.
    /// </summary>
    [CollectionDefinition(Name)]
    public class GitModuleTempRoot
    {
        public const string Name = "Git module temp root";
    }
}
