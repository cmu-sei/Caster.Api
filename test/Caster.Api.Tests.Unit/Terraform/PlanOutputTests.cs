// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Text.Json;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Serialization;
using TUnit.Core;

namespace Caster.Api.Tests.Unit.Terraform
{
    [Category("Unit")]
    [ClassDataSource<PlanFixture>(Shared = SharedType.PerTestSession)]
    public class PlanOutputTests(PlanFixture planFixture)
    {
        private readonly PlanFixture _planFixture = planFixture;
        private readonly PlanOutput _planOutput = planFixture.GetPlanOutput();

        [Test]
        public async Task ResourceChanges_WhenDeserialized_ReturnsExpectedCount()
        {
            await Assert.That(_planOutput.ResourceChanges.Count()).IsEqualTo(10);
        }

        [Test]
        public async Task GetAddedMachines_WhenCalled_ReturnsExpectedCount()
        {
            await Assert.That(_planFixture.GetPlanOutput().GetAddedMachines().Count()).IsEqualTo(7);
        }
    }

    public class PlanFixture
    {
        private readonly string _rawPlanOutput;
        public readonly PlanOutput _planOutput;

        public PlanFixture()
        {
            _rawPlanOutput = System.IO.File.ReadAllText(System.IO.Path.Combine(Environment.CurrentDirectory, "Data", "plan.json"));
            _planOutput = JsonSerializer.Deserialize<PlanOutput>(_rawPlanOutput, DefaultJsonSettings.Settings);
        }

        public PlanOutput GetPlanOutput()
        {
            return _planOutput;
        }
    }
}
