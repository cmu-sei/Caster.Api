// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;

namespace Caster.Api.Infrastructure.Exceptions
{
    public class TerraformCommandConflictException : Exception
    {
        public string Command { get; set; }

        public TerraformCommandConflictException(string command)
         : base("A conflicting Terraform command is already in progress")
        {
            Command = command;
        }
    }
}
