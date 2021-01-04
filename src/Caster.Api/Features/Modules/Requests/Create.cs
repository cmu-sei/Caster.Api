// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using AutoMapper;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;

namespace Caster.Api.Features.Modules
{
    public class Create
    {
        [DataContract(Name="CreateModuleCommand")]
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

        public class Handler : IRequestHandler<Command, Module>
        {
            private readonly CasterContext _db;
            private readonly IMapper _mapper;
            private readonly IAuthorizationService _authorizationService;
            private readonly ClaimsPrincipal _user;

            public Handler(
                CasterContext db,
                IMapper mapper,
                IAuthorizationService authorizationService,
                IIdentityResolver identityResolver)
            {
                _db = db;
                _mapper = mapper;
                _authorizationService = authorizationService;
                _user = identityResolver.GetClaimsPrincipal();
            }

            public async Task<Module> Handle(Command request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                var module = _mapper.Map<Domain.Models.Module>(request);

                await _db.Modules.AddAsync(module, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
                return _mapper.Map<Module>(module);
            }

        }
    }
}

