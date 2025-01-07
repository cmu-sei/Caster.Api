// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using Caster.Api.Infrastructure.Authorization;
using Microsoft.EntityFrameworkCore;
using FluentValidation;
using System.Text.Json.Serialization;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Vlan
{
    public class EditPool
    {
        [DataContract(Name = "EditPoolCommand")]
        public class Command : IRequest<Pool>
        {
            [JsonIgnore]
            public Guid Id { get; set; }

            /// <summary>
            /// The Name of the Pool
            /// </summary>
            [DataMember]
            public string Name { get; set; }
        }

        public class Validator : AbstractValidator<Command>
        {
            public Validator(IValidationService validationService)
            {
                RuleFor(x => x.Id).PoolExists(validationService);
            }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Command, Pool>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ManageVLANs], cancellationToken);

            public override async Task<Pool> HandleRequest(Command command, CancellationToken cancellationToken)
            {
                var pool = await dbContext.Pools
                    .Where(x => x.Id == command.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                mapper.Map(command, pool);
                await dbContext.SaveChangesAsync();

                return mapper.Map<Pool>(pool);
            }
        }
    }
}
