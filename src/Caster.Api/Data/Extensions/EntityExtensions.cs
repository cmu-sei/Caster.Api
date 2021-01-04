// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Domain.Events;
using Caster.Api.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Caster.Api.Data.Extensions
{
    public static class EntityExtensions
    {
        public static INotification ToEvent(this EntityEntry entry)
        {
            Type type = entry.Entity.GetType();
            INotification evt = null;

            if (type == typeof(Directory))
            {
                evt = new DirectoryUpdated(((Directory)entry.Entity).Id);
            }
            else if (type == typeof(Workspace))
            {
                evt = new WorkspaceUpdated(((Workspace)entry.Entity).Id);
            }
            else if (type == typeof(File))
            {
                evt = new FileUpdated(((File)entry.Entity).Id, true);
            }

            return evt;
        }
    }
}
