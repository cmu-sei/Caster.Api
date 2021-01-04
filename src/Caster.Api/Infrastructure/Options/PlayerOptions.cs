// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Caster.Api.Infrastructure.Options
{
    public class PlayerOptions
    {
        public string VmApiUrl { get; set; }
        public string VmConsoleUrl { get; set; }
        public int MaxParallelism { get; set; }
        public int RemoveLoopSeconds { get; set; }
    }
}
