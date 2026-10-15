// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Features.Inventory;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using NSubstitute;
using Xunit;

namespace Caster.Api.Tests.Unit.Inventory
{
    [Trait("Category", "Unit")]
    public class InventoryConnectionRequestTests
    {
        private readonly IInventoryService _inventory = Substitute.For<IInventoryService>();
        private readonly ICasterAuthorizationService _authorization = Substitute.For<ICasterAuthorizationService>();

        public InventoryConnectionRequestTests()
        {
            _inventory.GetSnapshot(Arg.Any<string>()).Returns((InventorySnapshot)null);
            _inventory.GetSnapshot("lab2").Returns(new InventorySnapshot
            {
                Isos = InventoryCategoryResult.Ok([new InventoryEntry { Id = "local:iso/a.iso", Name = "local:iso/a.iso" }])
            });
            _inventory.HasConnection("lab2").Returns(true);
        }

        [Fact]
        public async Task Get_ReadsTheNamedConnection()
        {
            var sut = new GetIsos.Handler(_authorization, _inventory);

            var result = await sut.HandleRequest(new GetIsos.Query { Connection = "lab2" }, CancellationToken.None);

            Assert.Equal("local:iso/a.iso", Assert.Single(result.Items).Id);
        }

        [Fact]
        public async Task Get_UnknownConnection_IsNotFound()
        {
            var sut = new GetIsos.Handler(_authorization, _inventory);

            var ex = await Assert.ThrowsAsync<EntityNotFoundException<InventoryConnection>>(
                () => sut.HandleRequest(new GetIsos.Query { Connection = "lab3" }, CancellationToken.None));

            Assert.Equal("Inventory connection 'lab3' is not configured.", ex.Message);
        }

        [Fact]
        public async Task Refresh_UnknownConnection_IsNotFoundAndRefreshesNothing()
        {
            var sut = new Refresh.Handler(_authorization, _inventory);

            await Assert.ThrowsAsync<EntityNotFoundException<InventoryConnection>>(
                () => sut.HandleRequest(new Refresh.Command { Connection = "lab3" }, CancellationToken.None));

            await _inventory.DidNotReceiveWithAnyArgs().ForceRefreshAsync(default, default, default);
        }

        [Fact]
        public async Task Refresh_RefreshesTheNamedConnection()
        {
            _inventory.ForceRefreshAsync("lab2", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(true);
            var sut = new Refresh.Handler(_authorization, _inventory);

            var result = await sut.HandleRequest(new Refresh.Command { Connection = "lab2" }, CancellationToken.None);

            Assert.True(result.Completed);
        }
    }
}
