// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Runtime.Serialization;

namespace Caster.Api.Features.Runs
{
    public class RunQuery
    {
        /// <summary>
        /// Whether to include the Plan resource with the Run
        /// </summary>
        [DataMember]
        public bool IncludePlan { get; set; }

        /// <summary>
        /// Whether  to include the Apply resource with the Run
        /// </summary>
        [DataMember]
        public bool IncludeApply { get; set; }
    }
}

