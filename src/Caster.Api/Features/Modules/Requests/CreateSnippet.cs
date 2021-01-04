// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MediatR;
using Caster.Api.Data;
using AutoMapper;
using System.Runtime.Serialization;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Identity;
using System.Linq;

namespace Caster.Api.Features.Modules
{
    public class CreateSnippet
    {
        [DataContract(Name="CreateSnippetCommand")]
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

        public class Handler : IRequestHandler<Command, string>
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

            public async Task<string> Handle(Command request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                var version = await _db.ModuleVersions.FirstOrDefaultAsync(v => v.Id == request.VersionId);
                var snippet = $"module \"{request.ModuleName}\" {{\n" +
                              $"  source = \"git::{version.UrlLink}?ref={version.Name}\"";
                foreach (var variable in request.VariableValues)
                {
                    var v = version.Variables.Where(v => v.Name == variable.Name).SingleOrDefault();
                    var value = variable.Value;

                    if (v != null && v.RequiresQuotes)
                    {
                        value = $"\"{value}\"";
                    }

                    snippet = $"{snippet}\n  {variable.Name} = {value}";
                }
                snippet = snippet + "\n}";

                return snippet;
            }

        }

        public class VariableValue
        {
            public string Name { get; set; }
            public string Value { get; set; }
        }
    }
}
