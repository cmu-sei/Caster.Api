// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;
using System.Text.RegularExpressions;

namespace Caster.Api.Infrastructure.Exceptions
{
    public class WorkspaceConflictException : ConflictException
    {
        public WorkspaceConflictException()
            : base("Only one operation can be performed on a Workspace at a time")
        {
        }
    }
}
