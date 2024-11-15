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
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Authorization;
using System.Linq;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Modules
{
    public class Get
    {
        [DataContract(Name = "GetModuleQuery")]
        public class Query : IRequest<Module>
        {
            /// <summary>
            /// The Id of the Module to retrieve
            /// </summary>
            [DataMember]
            public Guid Id { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, Module>
        {
            public override async Task Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermissions.ViewModules], cancellationToken);

            public override async Task<Module> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                var module = await dbContext.Modules
                    .Include(m => m.Versions)
                    .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken);

                if (module == null)
                    throw new EntityNotFoundException<Module>();

                module.Versions = module.Versions.OrderByDescending(x => x.DateCreated).ToList();

                return mapper.Map<Module>(module);
            }
        }
    }
}
