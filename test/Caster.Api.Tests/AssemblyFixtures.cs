// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// The run-wide factory variant: CasterAppFactory is one host for the whole run (agent-docs/api-testing).

using Caster.Api.Tests.Support;
using Xunit;

// Starting a PostgreSQL container and running Caster's migrations costs seconds, so it happens once for the
// whole assembly. xUnit v3 constructs this before the first test and injects it into any test class
// with a matching constructor parameter. The container itself starts on the first test that asks for a
// database (see PostgresTestDatabase), so tests that need none run without Docker.
[assembly: AssemblyFixture(typeof(DatabaseFixture))]

// Starting the application costs about a second, and everything it registers as a singleton is shared
// by every test that uses it: see CasterAppFactory, which says how each shared surface is dealt with.
[assembly: AssemblyFixture(typeof(CasterAppFactory))]
