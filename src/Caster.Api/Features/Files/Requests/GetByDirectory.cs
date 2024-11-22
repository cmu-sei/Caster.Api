// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Runtime.Serialization;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Authorization;
using System.Text.Json.Serialization;
using Caster.Api.Features.Shared;
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;

namespace Caster.Api.Features.Files
{
    public class GetByDirectory
    {
        [DataContract(Name = "GetFilesByDirectoryQuery")]
        public class Query : IRequest<File[]>
        {
            [JsonIgnore]
            public Guid DirectoryId { get; set; }

            /// <summary>
            /// Whether or not to retrieve file content.
            /// </summary>
            [DataMember]
            public bool IncludeContent { get; set; }

            /// <summary>
            /// Whether or not to retrieve deleted files.
            /// </summary>
            [DataMember]
            public bool IncludeDeleted { get; set; }
        }

        public class CommandValidator : AbstractValidator<Query>
        {
            public CommandValidator(IValidationService validationService)
            {
                RuleFor(x => x.DirectoryId).DirectoryExists(validationService);
            }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, File[]>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Domain.Models.Directory>(request.DirectoryId, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], cancellationToken);

            public override async Task<File[]> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                return await dbContext.Files
                    .GetAll(
                        configurationProvider: mapper.ConfigurationProvider,
                        directoryId: request.DirectoryId,
                        includeDeleted: request.IncludeDeleted,
                        includeContent: request.IncludeContent)
                    .ToArrayAsync(cancellationToken);
            }
        }
    }
}

