// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using System.Runtime.Serialization;
using AutoMapper;
using Caster.Api.Data;
using System;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Hosts
{
    public class Create
    {
        [DataContract(Name = "CreateHostCommand")]
        public class Command : IRequest<Host>
        {
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
            public int MaximumMachines { get; set; }

            /// <summary>
            /// True if this Host should be selected for deployments
            /// </summary>
            [DataMember]
            public bool Enabled { get; set; }

            /// <summary>
            /// True if this Host should not be automatically selected for deployments and only used for development purposes, manually
            /// </summary>
            [DataMember]
            public bool Development { get; set; }

            /// <summary>
            /// The Id of the Project to assign this Host to
            /// </summary>
            [DataMember]
            public Guid? ProjectId { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Command, Host>
        {
            public override async Task Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ManageHosts], cancellationToken);

            public override async Task<Host> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var host = mapper.Map<Domain.Models.Host>(request);
                dbContext.Hosts.Add(host);
                await dbContext.SaveChangesAsync(cancellationToken);
                return mapper.Map<Host>(host);
            }
        }
    }
}
