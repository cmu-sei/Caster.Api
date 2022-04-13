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

namespace Caster.Api.Features.Designs;

public class Create
{
    [DataContract(Name = "CreateDesignCommand")]
    public record Command : IRequest<Design>
    {
        /// <summary>
        /// Name of the design.
        /// </summary>
        [DataMember]
        public string Name { get; init; }

        /// <summary>
        /// ID of the directory this design is under.
        /// </summary>
        [DataMember]
        public Guid DirectoryId { get; init; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator(IValidationService validationService)
        {
            RuleFor(x => x.DirectoryId).DirectoryExists(validationService);
        }
    }

    public class Handler : BaseHandler<Handler>, IRequestHandler<Command, Design>
    {
        public Handler(IDependencyAggregate<Handler> aggregate) : base(aggregate) { }

        public async Task<Design> Handle(Command request, CancellationToken cancellationToken)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                throw new ForbiddenException();

            var design = _mapper.Map<Domain.Models.Design>(request);

            _db.Designs.Add(design);
            await _db.SaveChangesAsync(cancellationToken);

            return _mapper.Map<Design>(design);
        }
    }
}
