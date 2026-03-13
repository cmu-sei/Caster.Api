// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Domain.Models;
using TUnit.Core;

namespace Caster.Api.Tests.Unit.Models
{
    [Category("Unit")]
    public class VlanTests
    {
        [Test]
        public async Task SetReserved_WhenReservedEditableTrue_CanBeSetToTrue()
        {
            var vlan = new Vlan { ReservedEditable = true };

            vlan.Reserved = true;

            await Assert.That(vlan.Reserved).IsTrue();
        }

        [Test]
        public async Task SetReserved_WhenReservedEditableTrue_CanBeSetToFalse()
        {
            var vlan = new Vlan { ReservedEditable = true, Reserved = true };

            vlan.Reserved = false;

            await Assert.That(vlan.Reserved).IsFalse();
        }

        [Test]
        public async Task SetReserved_WhenNotEditableAndValueChanges_ThrowsArgumentException()
        {
            var vlan = new Vlan { ReservedEditable = true, Reserved = true };
            vlan.ReservedEditable = false;

            await Assert.That(() => vlan.Reserved = false)
                .ThrowsExactly<ArgumentException>();
        }

        [Test]
        public async Task SetReserved_WhenNotEditableAndValueUnchanged_DoesNotThrow()
        {
            var vlan = new Vlan { ReservedEditable = true, Reserved = true };
            vlan.ReservedEditable = false;

            vlan.Reserved = true; // Same value, should not throw

            await Assert.That(vlan.Reserved).IsTrue();
        }

        [Test]
        public async Task Constructor_WhenCreatingNewVlan_SetsDefaultValuesCorrectly()
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
