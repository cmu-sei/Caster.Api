// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Caster.Api.Infrastructure.Options
{
    public class TelemetryOptions
    {
        public bool AddAspNetCoreInstrumentation { get; set; }
        public bool AddHttpClientInstrumentation { get; set; }
        public bool UseMeterMicrosoftAspNetCoreHosting { get; set; }
        public bool UseMeterMicrosoftAspNetCoreServerKestrel { get; set; }
        public bool UseMeterSystemNetHttp { get; set; }
        public bool UseMeterSystemNetNameResolution { get; set; }
    }
}
