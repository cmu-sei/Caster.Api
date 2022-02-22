// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using System.Runtime.Serialization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Domain.Services;
using Caster.Api.Infrastructure.Identity;
using System;
using System.Linq;

namespace Caster.Api.Features.Modules
{
    public class GetAll
    {
        [DataContract(Name = "GetModulesQuery")]
        public class Query : IRequest<Module[]>
        {
            /// <summary>
            /// Whether or not to retrieve the module versions.
            /// </summary>
            [DataMember]
            public bool IncludeVersions { get; set; }

            /// <summary>
            /// force module update by ignoring DateModified.
            /// </summary>
            [DataMember]
            public bool ForceUpdate { get; set; }

            /// <summary>
            /// only return Modules in use by the specified Design
            /// </summary>
            [DataMember]
            public Guid? DesignId { get; set; }
        }

        public class Handler : IRequestHandler<Query, Module[]>
        {
            private readonly CasterContext _db;
            private readonly IMapper _mapper;
            private readonly IAuthorizationService _authorizationService;
            private readonly ClaimsPrincipal _user;
            private readonly IGitlabRepositoryService _gitlabRepositoryService;

            public Handler(
                CasterContext db,
                IMapper mapper,
                IGitlabRepositoryService gitlabRepositoryService,
                IAuthorizationService authorizationService,
                IIdentityResolver identityResolver)
            {
                _db = db;
                _mapper = mapper;
                _gitlabRepositoryService = gitlabRepositoryService;
                _authorizationService = authorizationService;
                _user = identityResolver.GetClaimsPrincipal();
            }

            public async Task<Module[]> Handle(Query request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                // TODO: add handling for other repositories?
                // get all modules from the repository and update the database
                await _gitlabRepositoryService.GetModulesAsync(request.ForceUpdate, cancellationToken);

                IQueryable<Domain.Models.Module> query = _db.Modules;

                if (request.DesignId.HasValue)
                {
                    var designQuery = _db.DesignModules
                        .Include(x => x.Module)
                        .Where(x => x.DesignId == request.DesignId);

                    if (request.IncludeVersions)
                    {
                        designQuery = designQuery
                            .Include(x => x.Module)
                            .ThenInclude(x => x.Versions);
                    }

                    query = designQuery.Select(x => x.Module);
                }
                else if (request.IncludeVersions)
                {
                    query = query.Include(x => x.Versions);
                }

                var modules = await query.ToArrayAsync(cancellationToken);
                return _mapper.Map<Module[]>(modules);
            }
        }
    }
}

