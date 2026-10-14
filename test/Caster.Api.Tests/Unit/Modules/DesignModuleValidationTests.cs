// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using DesignModuleValidation = Caster.Api.Features.DesignModules.DesignModuleValidation;
using Caster.Api.Infrastructure.Exceptions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Caster.Api.Tests.Unit.Modules
{
    [Trait("Category", "Unit")]
    [Trait("Category", "Modules")]
    public class DesignModuleValidationTests
    {
        private readonly CasterContext _context;
        private readonly Guid _moduleId = Guid.NewGuid();
        private readonly Guid _designId = Guid.NewGuid();

        public DesignModuleValidationTests()
        {
            var builder = new DbContextOptionsBuilder<CasterContext>();
            builder.UseInMemoryDatabase($"caster_test_{Guid.NewGuid()}");
            _context = new CasterContext(builder.Options);

            _context.ModuleVersions.Add(new ModuleVersion
            {
                ModuleId = _moduleId,
                Name = "v1.0.0",
                Variables =
                [
                    new ModuleVariable { Name = "vm_name", VariableType = "string" },
                    new ModuleVariable { Name = "cpus", VariableType = "number", DefaultValue = "2" },
                ]
            });
            _context.SaveChanges();
        }

        private DesignModule NewDesignModule(string version, params ModuleValue[] values) => new()
        {
            DesignId = _designId,
            ModuleId = _moduleId,
            Name = "vm",
            ModuleVersion = version,
            Values = new List<ModuleValue>(values)
        };

        [Fact]
        public async Task EnsureComplete_Rejects_Missing_Required_Value()
        {
            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                DesignModuleValidation.EnsureComplete(_context, NewDesignModule("v1.0.0"), CancellationToken.None));

            Assert.Contains("vm_name", string.Join(" ", ex.Errors.Values.SelectMany(x => x)));
        }

        [Fact]
        public async Task EnsureComplete_Rejects_Unknown_Version()
        {
            await Assert.ThrowsAsync<ValidationException>(() =>
                DesignModuleValidation.EnsureComplete(_context,
                    NewDesignModule("v9.9.9", new ModuleValue { Name = "vm_name", Value = "web" }), CancellationToken.None));
        }

        [Fact]
        public async Task EnsureComplete_Rejects_Name_Already_In_Design()
        {
            var existing = NewDesignModule("v1.0.0", new ModuleValue { Name = "vm_name", Value = "web" });
            _context.DesignModules.Add(existing);
            _context.SaveChanges();

            var duplicate = NewDesignModule("v1.0.0", new ModuleValue { Name = "vm_name", Value = "db" });
            duplicate.DesignId = existing.DesignId;

            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                DesignModuleValidation.EnsureComplete(_context, duplicate, CancellationToken.None));

            Assert.True(ex.Errors.ContainsKey(nameof(DesignModule.Name)));

            // the module itself, on Edit, is not a duplicate of its own name
            await DesignModuleValidation.EnsureComplete(_context, existing, CancellationToken.None);
        }

        [Fact]
        public async Task EnsureComplete_Accepts_Required_Values_Set()
        {
            await DesignModuleValidation.EnsureComplete(_context,
                NewDesignModule("v1.0.0", new ModuleValue { Name = "vm_name", Value = "web" }), CancellationToken.None);
        }
    }
}
