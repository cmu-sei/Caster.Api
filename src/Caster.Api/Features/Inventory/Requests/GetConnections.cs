// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using Caster.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Features.Shared;
using Caster.Api.Infrastructure.Authorization;
using MediatR;

namespace Caster.Api.Features.Inventory
{
    public class GetConnections
    {
        [DataContract(Name = "GetInventoryConnectionsQuery")]
        public class Query : IRequest<InventoryConnection[]>
        {
        }

        public class Handler(
            ICasterAuthorizationService authorizationService,
            IInventoryService inventoryService,
            CasterContext dbContext) : BaseHandler<Query, InventoryConnection[]>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ViewInfrastructure], cancellationToken);

            /// <summary>Each connection with the projects it is assigned to, for the Hypervisors page.</summary>
            public override async Task<InventoryConnection[]> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                var assignments = (await dbContext.ProjectInventoryConnections
                        .AsNoTracking()
                        .Select(x => new { x.NormalizedConnectionName, x.ProjectId, x.Project.Name })
                        .ToListAsync(cancellationToken))
                    .ToLookup(x => x.NormalizedConnectionName);

                return inventoryService.GetConnections()
                    .Select(info =>
                    {
                        var connection = InventoryConnection.From(info);
                        connection.Projects = assignments[ProjectInventoryConnection.Normalize(info.Name)]
                            .Select(x => new InventoryConnectionProject { ProjectId = x.ProjectId, Name = x.Name })
                            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                            .ToArray();
                        return connection;
                    })
                    .ToArray();
            }
        }
    }
}
