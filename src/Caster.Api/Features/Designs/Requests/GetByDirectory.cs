// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using System.Text.Json.Serialization;
using System.Linq;
using Caster.Api.Features.Shared;
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;

namespace Caster.Api.Features.Designs;

public class GetByDirectory
{
    [DataContract(Name = "GetDesignsByDirectoryQuery")]
    public record Query : IRequest<Design[]>
    {
        [JsonIgnore]
        public Guid DirectoryId { get; set; }
    }

    public class Validator : AbstractValidator<Query>
    {
        public Validator(IValidationService validationService)
        {
            RuleFor(x => x.DirectoryId).DirectoryExists(validationService);
        }
    }

    public class Handler : BaseHandler<Handler>, IRequestHandler<Query, Design[]>
    {
        public Handler(IDependencyAggregate<Handler> aggregate) : base(aggregate) { }

        public async Task<Design[]> Handle(Query request, CancellationToken cancellationToken)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                throw new ForbiddenException();

            var designs = await _db.Designs
                .Where(x => x.DirectoryId == request.DirectoryId)
                .ToArrayAsync();

            return _mapper.Map<Design[]>(designs);
        }
    }
}