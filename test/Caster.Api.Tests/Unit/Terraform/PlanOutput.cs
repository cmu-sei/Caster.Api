// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Text.Json;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Serialization;
using Xunit;
using File = System.IO.File;

namespace Caster.Api.Tests.Unit
{
    [Trait("Category", "Unit")]
    [Trait("Category", "TerraformPlanOutput")]
    public class TerraformPlanOutputUnitTest : IClassFixture<PlanFixture>
    {
        private readonly PlanFixture _planFixture;
        private readonly PlanOutput _planOutput;

        public TerraformPlanOutputUnitTest(PlanFixture planFixture)
        {
            _planFixture = planFixture;
            _planOutput = planFixture.GetPlanOutput();
        }

        [Fact]
        public void Test_Resource_Count()
        {
            Assert.Equal(10, _planOutput.ResourceChanges.Count());
        }

        [Fact]
        public void Test_New_Vm_Count()
        {
            Assert.Equal(7, _planFixture.GetPlanOutput().GetAddedMachines().Count());
        }

    }

    public class PlanFixture
    {
        private readonly string _rawPlanOutput;
        public readonly PlanOutput _planOutput;

        public PlanFixture()
        {
            _rawPlanOutput = File.ReadAllText($"{Environment.CurrentDirectory}\\Data\\plan.json");
            _planOutput = JsonSerializer.Deserialize<PlanOutput>(_rawPlanOutput, DefaultJsonSettings.Settings);
        }

        public PlanOutput GetPlanOutput()
        {
            return _planOutput;
        }
    }
}
