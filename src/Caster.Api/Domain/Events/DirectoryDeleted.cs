// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Domain.Models;
using MediatR;

namespace Caster.Api.Domain.Events
{
    public class DirectoryDeleted : INotification
    {
        public Directory Directory { get; set; }

        public DirectoryDeleted(Directory directory)
        {
            Directory = directory;
        }
    }
}
