// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Files
{
    public class Get
    {
        [DataContract(Name = "GetFileQuery")]
        public class Query : IRequest<File>
        {
            /// <summary>
            /// The Id of the File to retrieve
            /// </summary>
            [DataMember]
            public Guid Id { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IGetFileQuery fileQuery) : BaseHandler<Query, File>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Domain.Models.File>(request.Id, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], cancellationToken);

            public override async Task<File> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                var file = await fileQuery.ExecuteAsync(request.Id);

                if (file == null)
                    throw new EntityNotFoundException<File>();

                return file;
            }
        }
    }
}

