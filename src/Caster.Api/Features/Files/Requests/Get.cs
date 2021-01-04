// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Data;

namespace Caster.Api.Features.Files
{
    public class Get
    {
        [DataContract(Name="GetFileQuery")]
        public class Query : IRequest<File>
        {
            /// <summary>
            /// The Id of the File to retrieve
            /// </summary>
            [DataMember]
            public Guid Id { get; set; }
        }

        public class Handler : IRequestHandler<Query, File>
        {
            private readonly IGetFileQuery _fileQuery;
            private readonly IAuthorizationService _authorizationService;
            private readonly ClaimsPrincipal _user;

            public Handler(
                IGetFileQuery fileQuery,
                IAuthorizationService authorizationService,
                IIdentityResolver identityResolver)
            {
                _fileQuery = fileQuery;
                _authorizationService = authorizationService;
                _user = identityResolver.GetClaimsPrincipal();
            }

            public async Task<File> Handle(Query request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                var file = await _fileQuery.ExecuteAsync(request.Id);

                if (file == null)
                    throw new EntityNotFoundException<File>();

                return file;
            }
        }
    }
}

