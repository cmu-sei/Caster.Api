// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Domain.Models;
using Directory = Caster.Api.Domain.Models.Directory;
using File = Caster.Api.Domain.Models.File;
using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace Caster.Api.Tests.Unit.Models
{
    [Category("Unit")]
    [Category("Vlan")]
    public class VlanTests
    {
        [Test]
        public async Task Reserved_WhenReservedEditable_CanBeSet()
        {
            var vlan = new Vlan { ReservedEditable = true };

            vlan.Reserved = true;

            await Assert.That(vlan.Reserved).IsTrue();
        }

        [Test]
        public async Task Reserved_WhenReservedEditable_CanBeCleared()
        {
            var vlan = new Vlan { ReservedEditable = true, Reserved = true };

            vlan.Reserved = false;

            await Assert.That(vlan.Reserved).IsFalse();
        }

        [Test]
        public async Task Reserved_WhenNotEditableAndValueChanges_ThrowsArgumentException()
        {
            var vlan = new Vlan { ReservedEditable = true, Reserved = true };
            vlan.ReservedEditable = false;

            await Assert.That(() => vlan.Reserved = false)
                .ThrowsExactly<ArgumentException>();
        }

        [Test]
        public async Task Reserved_WhenNotEditableAndValueSame_DoesNotThrow()
        {
            var vlan = new Vlan { ReservedEditable = true, Reserved = true };
            vlan.ReservedEditable = false;

            vlan.Reserved = true; // Same value, should not throw

            await Assert.That(vlan.Reserved).IsTrue();
        }

        [Test]
        public async Task DefaultValues_AreCorrect()
        {
            var vlan = new Vlan();

            await Assert.That(vlan.InUse).IsFalse();
            await Assert.That(vlan.Reserved).IsFalse();
            await Assert.That(vlan.ReservedEditable).IsTrue();
            await Assert.That(vlan.Tag).IsNull();
            await Assert.That(vlan.PartitionId).IsNull();
        }
    }
}
