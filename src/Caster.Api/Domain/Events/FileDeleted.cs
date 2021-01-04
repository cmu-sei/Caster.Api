// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Domain.Models;
using MediatR;

namespace Caster.Api.Domain.Events
{
    public class FileDeleted : INotification
    {
        public File File { get; set; }

        public FileDeleted(File file)
        {
            File = file;
        }
    }
}
