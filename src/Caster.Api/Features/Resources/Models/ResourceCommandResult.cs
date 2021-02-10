// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Caster.Api.Features.Resources
{
    /// <summary>
    /// Result of a Resource command
    /// </summary>
    public class ResourceCommandResult
    {
        /// <summary>
        /// a list of the resulting resources after the command has been executed
        /// </summary>
        public Resource[] Resources { get; set; }

        /// <summary>
        /// a list of errors, if any, encountered during execution of the command
        /// </summary>
        public string[] Errors { get; set; }
    }
}
