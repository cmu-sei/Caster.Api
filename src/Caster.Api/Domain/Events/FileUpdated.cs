// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using MediatR;

namespace Caster.Api.Domain.Events
{
    public class FileUpdated : INotification
    {
        public Guid FileId { get;}
        public bool IncludeContent { get;}

        public FileUpdated(Guid fileId, bool includeContent)
        {
            FileId = fileId;
            IncludeContent = includeContent;
        }
    }
}
