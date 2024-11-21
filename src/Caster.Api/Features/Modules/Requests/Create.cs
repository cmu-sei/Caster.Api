// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using AutoMapper;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Modules
{
    public class Create
    {
        [DataContract(Name = "CreateModuleCommand")]
        public class Command : IRequest<Module>
        {
            /// <summary>
            /// ID of the Module.
            /// </summary>
            [DataMember]
            public string Id { get; set; }
            /// <summary>
            /// Name of the Module.
            /// </summary>
            [DataMember]
            public string Name { get; set; }
            /// <summary>
            /// Path of the Module.
            /// </summary>
            [DataMember]
            public string Path { get; set; }
            /// <summary>
            /// UrlLink of the Module.
            /// </summary>
            [DataMember]
            public string UrlLink { get; set; }
            /// <summary>
            /// Description of the Module.
            /// </summary>
            [DataMember]
            public string Description { get; set; }
            /// <summary>
            /// Versions of the Module.
            /// </summary>
            [DataMember]
            public List<string> Versions { get; set; }
            /// <summary>
            /// Variables of the Module.
            /// </summary>
            [DataMember]
            public List<ModuleVariable> Variables { get; set; }
            /// <summary>
            /// Outputs of the Module.
            /// </summary>
            [DataMember]
            public List<string> Outputs { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Command, Module>
        {
            public override async Task Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermissions.ManageModules], cancellationToken);

            public override async Task<Module> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var module = mapper.Map<Domain.Models.Module>(request);

                dbContext.Modules.Add(module);
                await dbContext.SaveChangesAsync(cancellationToken);
                return mapper.Map<Module>(module);
            }
        }
    }
}

