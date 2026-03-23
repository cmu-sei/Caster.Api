// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;

namespace Caster.Api.Features.Runs
{
    public class QueuePosition
    {
        public Guid ItemId { get; set; }
        public Guid WorkspaceId { get; set; }
        public int Position { get; set; }
    }
}
