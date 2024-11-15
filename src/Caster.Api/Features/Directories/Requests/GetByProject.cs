// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Runtime.Serialization;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Authorization;
using System.Text.Json.Serialization;
using Caster.Api.Features.Shared;
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;

namespace Caster.Api.Features.Directories
{
    public class GetByProject
    {
        [DataContract(Name = "GetDirectoriesByProjectQuery")]
        public class Query : IRequest<Directory[]>
        {
            [JsonIgnore]
            public Guid ProjectId { get; set; }

            /// <summary>
            /// Whether or not to return only top-level Directories
            /// </summary>
            [DataMember]
            public bool IncludeDescendants { get; set; }

            /// <summary>
            /// Whether or not to return related objects (Files, Workspaces)
            /// </summary>
            [DataMember]
            public bool IncludeRelated { get; set; }

            /// <summary>
            /// Whether or not to include contents of returned Files. Ignored if IncludeRelated is false
            /// </summary>
            [DataMember]
            public bool IncludeFileContent { get; set; }
        }

        public class Validator : AbstractValidator<Query>
        {
            public Validator(IValidationService validationService)
            {
                RuleFor(x => x.ProjectId).ProjectExists(validationService);
            }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, Directory[]>
        {
            public override async Task Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Project>(request.ProjectId, [SystemPermissions.ViewProjects], [ProjectPermissions.ViewProject], cancellationToken);

            public override async Task<Directory[]> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                var query = dbContext.Directories.Where(d => d.ProjectId == request.ProjectId);

                if (!request.IncludeDescendants)
                    query = query.Where(d => d.ParentId == null);

                var modifiedQuery = query.Expand(mapper.ConfigurationProvider, request.IncludeRelated, request.IncludeFileContent);
                var directories = await modifiedQuery.ToArrayAsync();

                return directories;
            }
        }
    }
}
