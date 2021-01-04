// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Caster.Api.Data;
using Caster.Api.Domain.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Caster.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Caster.Api.Features.Files.EventHandlers
{
    public class SignalRFileUpdatedHandler : INotificationHandler<FileUpdated>
    {
        private readonly CasterContext _db;
        private readonly IGetFileQuery _fileQuery;
        private readonly IHubContext<ProjectHub> _projectHub;

        public SignalRFileUpdatedHandler(
            CasterContext db,
            IGetFileQuery fileQuery,
            IHubContext<ProjectHub> projectHub)
        {
            _db = db;
            _fileQuery = fileQuery;
            _projectHub = projectHub;
        }

        public async Task Handle(FileUpdated notification, CancellationToken cancellationToken)
        {
            var file = await _fileQuery.ExecuteAsync(notification.FileId, notification.IncludeContent);

            var projectId = await _db.Directories
                .Where(d => d.Id == file.DirectoryId)
                .Select(d => d.ProjectId)
                .FirstOrDefaultAsync();

            await _projectHub.Clients.Group(projectId.ToString()).SendAsync("FileUpdated", file);
        }
    }
}
