// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Caster.Api.Infrastructure.Options
{
    public class FileVersionScrubOptions
    {
        public int DaysToSaveAllUntaggedVersions { get; set; }
        public int DaysToSaveDailyUntaggedVersions { get; set; }
    }
}
