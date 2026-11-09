// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.EnvironmentSets;
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Features.Shared;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.EnvironmentSets;

public class EditEntry
{
    [DataContract(Name = "EditEnvironmentSetEntryCommand")]
    public class Command : IRequest<EnvironmentSet>, IEnvironmentSetEntryFields
    {
        [JsonIgnore]
        public Guid EnvironmentSetId { get; set; }

        [JsonIgnore]
        public Guid Id { get; set; }

        [DataMember]
        public EnvironmentSetEntryKind Kind { get; set; }

        [DataMember]
        public string TargetName { get; set; }

        [DataMember]
        public EnvironmentSetValueSource ValueSource { get; set; }

        [DataMember]
        public string SecretName { get; set; }

        [DataMember]
        public string Value { get; set; }

        [DataMember]
        public string ConnectionName { get; set; }

        [DataMember]
        public ConnectionField? ConnectionField { get; set; }
    }

    public class Validator(ISecretSource secretSource, IInfrastructureConnectionCatalog connections)
        : EnvironmentSetEntryFieldsValidator<Command>(secretSource, connections);

    public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext, IEnvironmentSetViews views) : BaseHandler<Command, EnvironmentSet>
    {
        /// <summary>
        /// Editing an entry into, or out of, a connection entry also needs ManageInfrastructure,
        /// as creating one does.
        /// </summary>
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken)
        {
            var connection = request.ValueSource == EnvironmentSetValueSource.Connection ||
                await dbContext.EnvironmentSetEntries.AnyAsync(
                    x => x.Id == request.Id && x.ValueSource == EnvironmentSetValueSource.Connection, cancellationToken);

            return await authorizationService.Authorize([SystemPermission.ManageEnvironmentSets], cancellationToken) &&
                (!connection || await authorizationService.Authorize([SystemPermission.ManageInfrastructure], cancellationToken));
        }

        public override Task<EnvironmentSet> HandleRequest(Command request, CancellationToken cancellationToken) =>
            dbContext.WithEnvironmentSetLock(() => Handle(request, cancellationToken), cancellationToken);

        private async Task<EnvironmentSet> Handle(Command request, CancellationToken cancellationToken)
        {
            var set = await dbContext.EnvironmentSets
                .Include(x => x.Entries)
                .SingleOrDefaultAsync(x => x.Id == request.EnvironmentSetId, cancellationToken)
                ?? throw new EntityNotFoundException<EnvironmentSet>();

            var entry = set.Entries.SingleOrDefault(x => x.Id == request.Id)
                ?? throw new EntityNotFoundException<EnvironmentSetEntry>();

            var candidate = new Domain.Models.EnvironmentSetEntry { Id = entry.Id, EnvironmentSetId = set.Id };
            candidate.Apply(request);

            await dbContext.EnsureNoConflicts(
                set.Id,
                [.. set.Entries.Where(x => x.Id != entry.Id), candidate],
                null,
                cancellationToken);

            entry.Apply(request);
            await dbContext.SaveChangesAsync(cancellationToken);

            return await views.GetSet(set.Id, cancellationToken);
        }
    }
}
