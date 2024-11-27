// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using System.Runtime.Serialization;
using Caster.Api.Data;
using System;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Exceptions;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using System.Linq;
using Caster.Api.Infrastructure.Identity;
using System.Text.Json.Serialization;
using System.Web;

namespace Caster.Api.Features.Resources
{
    public class Get
    {
        [DataContract(Name = "GetResourceQuery")]
        public class Query : IRequest<Resource>
        {
            /// <summary>
            /// Id of the Workspace that the Resource exists in.
            /// </summary>
            [JsonIgnore]
            public Guid WorkspaceId { get; set; }

            /// <summary>
            /// Address of the Resource
            /// </summary>
            [JsonIgnore]
            public string Address { get; set; }
        }

        public class Handler : IRequestHandler<Query, Resource>
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

            public async Task<Resource> Handle(Query request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                var workspace = await _db.Workspaces.FindAsync(new object[] { request.WorkspaceId }, cancellationToken);

                if (workspace == null)
                    throw new EntityNotFoundException<Workspace>();

                var state = workspace.GetState();
                var resources = state.GetResources();
                var address = HttpUtility.UrlDecode(request.Address);

                var resource = resources.Where(r => r.Address == address).FirstOrDefault();
                if (resource == null)
                    throw new EntityNotFoundException<Resource>();

                workspace.SetResourceTaint(resource);

                return _mapper.Map<Resource>(resource, opts => opts.ExcludeMembers());
            }
        }
    }
}
