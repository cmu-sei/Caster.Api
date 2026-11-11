// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using MediatR;
using Caster.Api.Data;
using System.Runtime.Serialization;
using Microsoft.CodeAnalysis;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Domain.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Caster.Api.Infrastructure.Authorization;
using System.Security.Principal;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Features.Shared;

namespace Caster.Api.Features.Hosts
{
    public class PartialEdit
    {
        [DataContract(Name = "PartialEditHostCommand")]
        public class Command : IRequest<Host>
        {
            public Guid Id { get; set; }

            /// <summary>
            /// Name of the Host
            /// </summary>
            [DataMember]
            public string Name { get; set; }

            /// <summary>
            /// Name of the datastore to use with this Host
            /// </summary>
            [DataMember]
            public string Datastore { get; set; }

            /// <summary>
            /// Maximum number of mahcines to deploy to this Host
            /// </summary>
            [DataMember]
            public int? MaximumMachines { get; set; }

            /// <summary>
            /// True if this Host should be selected for deployments
            /// </summary>
            [DataMember]
            public bool? Enabled { get; set; }

            /// <summary>
            /// True if this Host should not be automatically selected for deployments and only used for development purposes, manually
            /// </summary>
            [DataMember]
            public bool? Development { get; set; }

            /// <summary>
            /// The Id of the Project to assign this Host to
            /// </summary>
            [DataMember]
            public Optional<Guid?> ProjectId { get; set; }

            /// <summary>
            /// The Id of the project group to assign this Host to. Setting a Project clears the
            /// group, and setting a group clears the Project.
            /// </summary>
            [DataMember]
            public Optional<Guid?> ProjectGroupId { get; set; }
        }

        public class Validator : AbstractValidator<Command>
        {
            public Validator()
            {
                RuleFor(x => x)
                    .Must(x => !(x.ProjectId.HasValue && x.ProjectId.Value.HasValue && x.ProjectGroupId.HasValue && x.ProjectGroupId.Value.HasValue))
                    .WithMessage("A Host belongs to a Project or a project group, not both.");
            }
        }

        public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext) : BaseHandler<Command, Host>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ManageHosts], cancellationToken);

            public override async Task<Host> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var host = await dbContext.Hosts.FindAsync([request.Id], cancellationToken);

                if (host == null)
                    throw new EntityNotFoundException<Host>();

                request.ApplyPatchTo(host);

                // A Host has one owner: the one just set replaces the other, whether the other was
                // left out or sent as null. The validator refuses both set at once.
                if (request.ProjectId.HasValue && request.ProjectId.Value.HasValue)
                    host.ProjectGroupId = null;
                else if (request.ProjectGroupId.HasValue && request.ProjectGroupId.Value.HasValue)
                    host.ProjectId = null;

                await dbContext.SaveChangesAsync(cancellationToken);
                return host.ToHost();
            }
        }
    }
}
