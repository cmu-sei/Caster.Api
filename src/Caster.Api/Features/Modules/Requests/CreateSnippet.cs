// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MediatR;
using Caster.Api.Data;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using System.Linq;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Services.ModuleScopes;

namespace Caster.Api.Features.Modules
{
    public class CreateSnippet
    {
        [DataContract(Name = "CreateSnippetCommand")]
        public class Command : IRequest<string>
        {
            /// <summary>
            /// ID of the Version.
            /// </summary>
            [DataMember]
            public Guid VersionId { get; set; }

            /// <summary>
            /// Name for this instance of the Module.
            /// </summary>
            [DataMember]
            public string ModuleName { get; set; }

            /// <summary>
            /// Variables of the Module.
            /// </summary>
            [DataMember]
            public List<VariableValue> VariableValues { get; set; }

            /// <summary>
            /// The project the snippet is made for. The Module must be one the project may use (its
            /// Module Scopes and its project group's), and the caller needs ViewProject on it (or
            /// ViewProjects) instead of ViewModules. Without it, the snippet is made for a holder
            /// of ViewModules for any Module.
            /// </summary>
            [DataMember]
            public Guid? ProjectId { get; set; }
        }

        public class Handler(
            ICasterAuthorizationService authorizationService,
            CasterContext dbContext,
            IModuleAccessService moduleAccess) : BaseHandler<Command, string>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ViewModules], cancellationToken)
                || (request.ProjectId.HasValue && await authorizationService.Authorize<Project>(
                    request.ProjectId, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], cancellationToken));

            public override async Task<string> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var version = await dbContext.ModuleVersions
                    .Include(v => v.Module)
                    .FirstOrDefaultAsync(v => v.Id == request.VersionId, cancellationToken);

                if (version == null)
                    throw new EntityNotFoundException<ModuleVersion>();

                // Naming a project limits anyone, a holder of ViewModules too, to what that project may
                // use, as for GET api/modules. Unlike a lookup, a Design that already uses the Module
                // does not open it: a snippet adds a new use. An outside Module is the same 404 as a
                // missing Version, so the answer does not say whether it exists.
                if (request.ProjectId.HasValue
                    && !(await moduleAccess.ForProject(request.ProjectId.Value, cancellationToken)).Allows(version.Module.Path))
                {
                    throw new EntityNotFoundException<ModuleVersion>();
                }

                return version.ToSnippet(request.ModuleName, VariableValue.ToDomain(request.VariableValues));
            }
        }

        public class VariableValue
        {
            public string Name { get; set; }
            public string Value { get; set; }

            public ModuleValue ToDomain()
            {
                return new ModuleValue
                {
                    Name = Name,
                    Value = Value
                };
            }

            public static IEnumerable<ModuleValue> ToDomain(IEnumerable<VariableValue> values)
            {
                return values.Select(x => x.ToDomain());
            }
        }
    }
}
