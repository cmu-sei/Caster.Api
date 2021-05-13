// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Runtime.Serialization;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Options;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Domain.Services;
using System.Linq;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Domain.Events;
using AutoMapper.QueryableExtensions;
using Caster.Api.Infrastructure.Extensions;
using System.Text.Json.Serialization;

namespace Caster.Api.Features.Runs
{
    public class Cancel
    {
        [DataContract(Name = "CancelRunCommand")]
        public class Command : IRequest<Run>
        {
            /// <summary>
            /// The Id of the Run to Cancel
            /// </summary>
            [JsonIgnore]
            public Guid Id { get; set; }

            /// <summary>
            /// If true, will forcefully terminate the Run.
            /// If false, will attempt to gracefully cancel the Run.
            /// </summary>
            [DataMember]
            public bool Force { get; set; }
        }

        public class Handler : IRequestHandler<Command, Run>
        {
            private readonly CasterContext _db;
            private readonly IMapper _mapper;
            private readonly IAuthorizationService _authorizationService;
            private readonly ClaimsPrincipal _user;
            private readonly ITerraformService _terraformService;

            public Handler(
                CasterContext db,
                IMapper mapper,
                IAuthorizationService authorizationService,
                IIdentityResolver identityResolver,
                ITerraformService terraformService)
            {
                _db = db;
                _mapper = mapper;
                _authorizationService = authorizationService;
                _user = identityResolver.GetClaimsPrincipal();
                _terraformService = terraformService;
            }

            public async Task<Run> Handle(Command request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                var run = await _db.Runs
                    .Where(x => x.Id == request.Id)
                    .Include(x => x.Workspace)
                    .SingleOrDefaultAsync(cancellationToken);

                ValidateRun(run);

                _terraformService.CancelRun(run.Workspace, request.Force);

                return await _db.Runs
                    .ProjectTo<Run>(_mapper.ConfigurationProvider)
                    .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
            }

            private void ValidateRun(Domain.Models.Run run)
            {
                if (run == null)
                    throw new EntityNotFoundException<Run>();

                if (run.Status != RunStatus.Applying && run.Status != RunStatus.Planning)
                {
                    throw new InvalidOperationException("Cannot cancel a Run without a Plan or Apply in progress");
                }
            }
        }
    }
}