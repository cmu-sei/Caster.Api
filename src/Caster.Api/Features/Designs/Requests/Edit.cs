// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Shared;
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

namespace Caster.Api.Features.Designs;

public class Edit
{
    [DataContract(Name = "EditDesignCommand")]
    public record Command : IRequest<Design>
    {
        [JsonIgnore]
        public Guid Id { get; set; }

        /// <summary>
        /// Name of the design.
        /// </summary>
        [DataMember]
        public string Name { get; init; }
    }

    public class Handler : BaseHandler<Handler>, IRequestHandler<Command, Design>
    {
        public Handler(IDependencyAggregate<Handler> aggregate) : base(aggregate) { }

        public async Task<Design> Handle(Command request, CancellationToken cancellationToken)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                throw new ForbiddenException();

            var design = await _db.Designs
                .Where(x => x.Id == request.Id)
                .SingleOrDefaultAsync(cancellationToken);

            if (design == null)
                throw new EntityNotFoundException<Design>();

            _mapper.Map(request, design);

            await _db.SaveChangesAsync(cancellationToken);

            return _mapper.Map<Design>(design);
        }
    }
}
