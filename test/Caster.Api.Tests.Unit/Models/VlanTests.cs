// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Domain.Models;
using Xunit;

namespace Caster.Api.Tests.Unit.Models
{
    [Trait("Category", "Unit")]
    public class VlanTests
    {
        [Fact]
        public void SetReserved_WhenReservedEditableTrue_CanBeSetToTrue()
        {
            var vlan = new Vlan { ReservedEditable = true };

            vlan.Reserved = true;

            Assert.True(vlan.Reserved);
        }

        [Fact]
        public void SetReserved_WhenReservedEditableTrue_CanBeSetToFalse()
        {
            var vlan = new Vlan { ReservedEditable = true, Reserved = true };

            vlan.Reserved = false;

            Assert.False(vlan.Reserved);
        }

        [Fact]
        public void SetReserved_WhenNotEditableAndValueChanges_ThrowsArgumentException()
        {
            var vlan = new Vlan { ReservedEditable = true, Reserved = true };
            vlan.ReservedEditable = false;

            Assert.Throws<ArgumentException>(() => vlan.Reserved = false);
        }

        [Fact]
        public void SetReserved_WhenNotEditableAndValueUnchanged_DoesNotThrow()
        {
            var vlan = new Vlan { ReservedEditable = true, Reserved = true };
            vlan.ReservedEditable = false;

            vlan.Reserved = true; // Same value, should not throw

            Assert.True(vlan.Reserved);
        }

        [Fact]
        public void Constructor_WhenCreatingNewVlan_SetsDefaultValuesCorrectly()
        {
            var vlan = new Vlan();

            Assert.False(vlan.InUse);
            Assert.False(vlan.Reserved);
            Assert.True(vlan.ReservedEditable);
            Assert.Null(vlan.Tag);
            Assert.Null(vlan.PartitionId);
        }
    }
}
