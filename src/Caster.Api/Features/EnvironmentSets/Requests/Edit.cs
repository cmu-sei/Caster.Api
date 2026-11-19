// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Shared;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using FluentValidation;
using MediatR;

namespace Caster.Api.Features.EnvironmentSets;

public class Edit
{
    [DataContract(Name = "EditEnvironmentSetCommand")]
    public class Command : IRequest<EnvironmentSet>
    {
        [JsonIgnore]
        public Guid Id { get; set; }

        [DataMember]
        public string Name { get; set; }

        [DataMember]
        public string Description { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(EnvironmentSetEntryFieldsValidator<IEnvironmentSetEntryFields>.MaximumNameLength);
        }
    }

    public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext, IEnvironmentSetViews views) : BaseHandler<Command, EnvironmentSet>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageEnvironmentSets], cancellationToken);

        public override async Task<EnvironmentSet> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var set = await dbContext.EnvironmentSets.FindAsync([request.Id], cancellationToken)
                ?? throw new EntityNotFoundException<EnvironmentSet>();

            var name = request.Name.Trim();
            await dbContext.EnsureUniqueSetName(name, set.Id, cancellationToken);

            set.Name = name;
            set.Description = request.Description;
            await dbContext.SaveChangesAsync(cancellationToken);

            return await views.GetSet(set.Id, cancellationToken);
        }
    }
}
