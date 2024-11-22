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

        }

        public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext) : BaseHandler<Command, string>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ViewModules], cancellationToken);

            public override async Task<string> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var version = await dbContext.ModuleVersions.FirstOrDefaultAsync(v => v.Id == request.VersionId);

                if (version == null)
                    throw new EntityNotFoundException<ModuleVersion>();

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
