// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Caster.Api.Infrastructure.Options
{
    public class TerraformOptions
    {
        public string BinaryPath { get; set; }
        public string DefaultVersion { get; set; }
        public string PluginDirectory { get; set; }
        public string RootWorkingDirectory { get; set; }
        public double OutputSaveInterval { get; set; }
        public string GitlabApiUrl { get; set; }
        public string GitlabToken { get; set; }
        public int? GitlabGroupId { get; set; }
        public int StateRetryCount { get; set; }
        public int StateRetryIntervalSeconds { get; set; }
    }
}
