// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Diagnostics.Metrics;

namespace Caster.Api.Domain.Services
{
    public interface ITelemetryService
    {
    }

    public class TelemetryService : ITelemetryService
    {
        public const string CasterMeterName = "cmu_sei_crucible_caster";
        public readonly Meter CasterMeter = new Meter(CasterMeterName, "1.0");
        public Gauge<int> Projects;
        public Gauge<int> Workspaces;

        public TelemetryService()
        {
            Projects = CasterMeter.CreateGauge<int>("caster_projects");
            Workspaces = CasterMeter.CreateGauge<int>("caster_workspaces");
        }

    }
}
