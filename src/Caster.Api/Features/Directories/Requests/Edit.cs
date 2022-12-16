// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using AutoMapper;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using System.Security.Claims;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Features.Directories.Interfaces;
using FluentValidation;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Features.Shared.Services;

namespace Caster.Api.Features.Directories
{
    public class Edit
    {
        [DataContract(Name = "EditDirectoryCommand")]
        public class Command : IRequest<Directory>, IDirectoryUpdateRequest
        {
            public Guid Id { get; set; }

            /// <summary>
            /// Name of the directory.
            /// </summary>
            [DataMember]
            public string Name { get; set; }

            /// <summary>
            /// Id of the directory that will be the parent of this directory
            /// </summary>
            [DataMember]
            public Guid? ParentId { get; set; }

            /// <summary>
            /// The version of Terraform that will be set Workspaces created in this Directory.
            /// If not set, will traverse parents until a version is found.
            /// If still not set, the default version will be used.
            /// </summary>
            [DataMember]
            public string TerraformVersion { get; set; }

            /// <summary>
            /// Limit the number of concurrent operations as Terraform walks the graph. 
            /// If not set, will traverse parents until a value is found.
            /// If still not set, the Terraform default will be used.
            /// </summary>
            [DataMember]
            public int? Parallelism { get; set; }
        }

        public class CommandValidator : AbstractValidator<Command>
        {
            public CommandValidator(IValidationService validationService)
            {
                RuleFor(x => x.ParentId.Value).DirectoryExists(validationService).When(x => x.ParentId.HasValue);
            }
        }

        public class Handler : BaseEdit.Handler, IRequestHandler<Command, Directory>
        {
            protected readonly IAuthorizationService _authorizationService;
            protected readonly ClaimsPrincipal _user;
            public Handler(CasterContext db, IMapper mapper, IAuthorizationService authorizationService, IIdentityResolver identityResolver) : base(db, mapper)
            {
                _authorizationService = authorizationService;
                _user = identityResolver.GetClaimsPrincipal();
            }

            public async Task<Directory> Handle(Command request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                var directory = await _db.Directories.FindAsync(request.Id);

                if (directory == null)
                    throw new EntityNotFoundException<Directory>();

                if (directory.ParentId != request.ParentId)
                {
                    await UpdatePaths(directory, request.ParentId);
                }

                _mapper.Map(request, directory);

                await _db.SaveChangesAsync();
                return _mapper.Map<Directory>(directory);
            }
        }
    }
}
