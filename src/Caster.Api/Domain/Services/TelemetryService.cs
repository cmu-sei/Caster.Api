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
        public const string CasterMeterName = "cmu_sei_caster_meter";
        public readonly Meter CasterMeter = new Meter(CasterMeterName, "1.0");
        public  Gauge<int> CasterGauge1;

        public TelemetryService()
        {
            CasterGauge1 = CasterMeter.CreateGauge<int>("caster_info");
        }

    }
}
