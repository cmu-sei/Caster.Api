// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Shared;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Serialization;
using FluentValidation;
using MediatR;

namespace Caster.Api.Features.Workspaces
{
    public class EditTopologyLayout
    {
        /// <summary>The most nodes one request may set, well past any workspace's topology.</summary>
        public const int MaxNodes = 2000;

        [DataContract(Name = "EditWorkspaceTopologyLayoutCommand")]
        public class Command : IRequest<Dictionary<string, TopologyNodeLayout>>
        {
            [JsonIgnore]
            public Guid Id { get; set; }

            /// <summary>
            /// The layout to save for each node, by node id. Each replaces what
            /// was saved for that node; nodes not named keep theirs. A null
            /// forgets the node's position and role.
            /// </summary>
            [DataMember]
            public Dictionary<string, TopologyNodeLayout> Nodes { get; set; } = [];
        }

        public class CommandValidator : AbstractValidator<Command>
        {
            public CommandValidator()
            {
                RuleFor(x => x.Nodes).NotNull();
                RuleFor(x => x.Nodes.Count)
                    .LessThanOrEqualTo(MaxNodes)
                    .When(x => x.Nodes != null)
                    .WithName("Nodes");
                RuleForEach(x => x.Nodes)
                    .Must(x => !string.IsNullOrWhiteSpace(x.Key))
                    .WithMessage("A node id must not be empty.")
                    .Must(x => x.Value == null || (IsPosition(x.Value.X) && IsPosition(x.Value.Y)))
                    .WithMessage("A node's position must be a finite number.")
                    .When(x => x.Nodes != null);
            }

            private static bool IsPosition(double? value) => value is null || double.IsFinite(value.Value);
        }

        public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext) : BaseHandler<Command, Dictionary<string, TopologyNodeLayout>>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Domain.Models.Workspace>(request.Id, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

            public override async Task<Dictionary<string, TopologyNodeLayout>> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var workspace = await dbContext.Workspaces.FindAsync([request.Id], cancellationToken)
                    ?? throw new EntityNotFoundException<Workspace>();

                var layout = Read(workspace.TopologyLayout);

                foreach (var (id, node) in request.Nodes)
                {
                    // Nothing left to remember is the same as never having moved it.
                    if (node == null || (node.X == null && node.Y == null && node.Role == null))
                        layout.Remove(id);
                    else
                        layout[id] = node;
                }

                workspace.TopologyLayout = layout.Count == 0
                    ? null
                    : JsonSerializer.Serialize(layout, DefaultJsonSettings.Settings);

                await dbContext.SaveChangesAsync(cancellationToken);

                return layout;
            }
        }

        /// <summary>The layout saved on a workspace, empty when there is none.</summary>
        public static Dictionary<string, TopologyNodeLayout> Read(string json) =>
            string.IsNullOrEmpty(json)
                ? []
                : JsonSerializer.Deserialize<Dictionary<string, TopologyNodeLayout>>(json, DefaultJsonSettings.Settings);
    }
}
