// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Caster.Api.Infrastructure.Exceptions
{
    /// <summary>
    /// No module source is configured at all, so there is nothing to list.
    /// Distinct from "synced successfully and found nothing", which used to be
    /// indistinguishable because the sync failure was swallowed.
    /// </summary>
    public class ModuleSourceNotConfiguredException : Exception, IApiException
    {
        public ModuleSourceNotConfiguredException()
            : base("No module source is configured. Set Terraform:ModuleSources to one or more git repositories, " +
                   "or set Terraform:GitlabApiUrl and Terraform:GitlabGroupId, then retry.")
        {
        }

        public ModuleSourceNotConfiguredException(string message)
            : base(message)
        {
        }

        // The server is reachable but cannot serve this feature until an
        // operator configures it.
        public HttpStatusCode GetStatusCode() => HttpStatusCode.ServiceUnavailable;
    }

    /// <summary>
    /// At least one configured module source failed to sync. Sources are
    /// independent, so whatever did sync is already persisted; this reports
    /// what did not.
    /// </summary>
    public class ModuleSyncException : Exception, IApiException
    {
        public IReadOnlyList<string> Failures { get; }

        public ModuleSyncException(IEnumerable<string> failures)
            : base(BuildMessage(failures))
        {
            Failures = failures?.ToArray() ?? [];
        }

        public ModuleSyncException(string message)
            : base(message)
        {
            Failures = [message];
        }

        private static string BuildMessage(IEnumerable<string> failures)
        {
            var list = failures?.ToArray() ?? [];

            return list.Length switch
            {
                0 => "Failed to sync Modules from the configured module sources.",
                1 => $"Failed to sync Modules: {list[0]}",
                _ => $"Failed to sync Modules from {list.Length} module sources: {string.Join("; ", list)}",
            };
        }

        public HttpStatusCode GetStatusCode() => HttpStatusCode.BadGateway;
    }
}
