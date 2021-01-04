// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using Microsoft.EntityFrameworkCore;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Infrastructure.Identity;

namespace Caster.Api.Features.Files
{
    public class Tag
    {
        [DataContract(Name="TagFileCommand")]
        public class Command : IRequest<FileVersion[]>
        {
            /// <summary>
            /// Tag to apply.
            /// </summary>
            [DataMember]
            public string Tag { get; set; }

            /// <summary>
            /// ID's of the files to tag.
            /// </summary>
            [DataMember]
            public Guid[] FileIds { get; set; }

        }

        public class Handler : IRequestHandler<Command, FileVersion[]>
        {
            private readonly CasterContext _db;
            private readonly IMapper _mapper;
            private readonly IAuthorizationService _authorizationService;
            private readonly ClaimsPrincipal _user;

            public Handler(
                CasterContext db,
                IMapper mapper,
                IAuthorizationService authorizationService,
                IIdentityResolver identityResolver)
            {
                _db = db;
                _mapper = mapper;
                _authorizationService = authorizationService;
                _user = identityResolver.GetClaimsPrincipal() as ClaimsPrincipal;
            }

            public async Task<FileVersion[]> Handle(Command request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                var dateTagged = DateTime.UtcNow;
                var tag = request.Tag;

                var files = await _db.Files
                    .Where(f => request.FileIds.Contains(f.Id))
                    .ToArrayAsync();

                foreach (var fileId in request.FileIds)
                {
                    var file = files.Where(f => f.Id == fileId).FirstOrDefault();
                    if (file == null)
                        throw new EntityNotFoundException<File>($"File {fileId} could not be found.");

                    file.Tag(tag, _user.GetId(), dateTagged);
                }

                await _db.SaveChangesAsync(cancellationToken);
                return await _db.FileVersions
                    .Where(fileVersion => fileVersion.Tag == request.Tag)
                    .ProjectTo<FileVersion>(_mapper.ConfigurationProvider)
                    .ToArrayAsync();
            }

        }
    }
}

