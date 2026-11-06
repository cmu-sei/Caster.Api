// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Text.RegularExpressions;

namespace Caster.Api.Domain.Services.Modules
{
    /// <summary>
    /// Makes a configured module source url safe to show. A source url may
    /// carry a token anywhere a user name or password can go, in any of git's
    /// url forms, and a token may hold any character. So instead of stripping
    /// what looks like a credential, this rebuilds the url from parts it can
    /// parse exactly and returns null for anything else: a missing link is
    /// acceptable, a leaked token is not.
    /// </summary>
    /// <remarks>
    /// The forms shown:
    /// <list type="bullet">
    /// <item><c>http(s)://host[:port]/path</c>, never with a user or password, without query or fragment.</item>
    /// <item><c>ssh://[git@]host[:port]/path</c>; any user but a bare <c>git</c> is dropped.</item>
    /// <item>scp-style <c>[git@]host:path</c>; any other user is dropped.</item>
    /// <item><c>file://</c> urls and local paths, as configured, if they hold no <c>@</c> and no further <c>://</c>.</item>
    /// </list>
    /// Wherever the path, query or fragment holds an <c>@</c> the url is not
    /// shown, because that <c>@</c> may end a credential the parse took for a
    /// host (<c>https://user:123?tok@host/</c> reads as host "user", port 123).
    /// </remarks>
    public enum ModuleSourceUrlCheck
    {
        Usable,
        Credentials,
        Unusable,
    }

    /// <summary>
    /// A module source whose url is refused (see <see cref="ModuleSourceUrl.Check"/>).
    /// A sync failure like any other, so it is recorded as the source's Error.
    /// </summary>
    public class ModuleSourceRefusedException(string message)
        : Infrastructure.Exceptions.ModuleSyncException(message);

    public static partial class ModuleSourceUrl
    {
        /// <summary>The one user name shown: the conventional ssh account, which is not a secret.</summary>
        public const string KeptUserName = "git";

        public const string CredentialsMessage =
            "This source's URL contains credentials. Put them in the git credential store (the chart's gitcredentials) instead.";

        public const string UnusableMessage = "This source's URL isn't a git URL Caster can use.";

        /// <summary>
        /// Whether a configured source url may be used at all. Only a url that
        /// <see cref="Sanitize"/> returns unchanged is: anything it would have
        /// to clean is refused rather than used, because a url is cloned,
        /// stored in every version's UrlLink, and echoed in git's errors. A
        /// refused url with an <c>@</c> beyond a bare <c>git@</c> is taken to
        /// carry credentials; any other is unusable.
        /// </summary>
        public static ModuleSourceUrlCheck Check(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return ModuleSourceUrlCheck.Unusable;
            }

            var trimmed = url.Trim();
            var clean = Sanitize(trimmed);

            // Lowercasing the scheme is the one change cleaning may make to a
            // usable url.
            var schemeLength = SchemeLength(trimmed);
            var normalized = trimmed[..schemeLength].ToLowerInvariant() + trimmed[schemeLength..];

            if (clean == normalized)
            {
                return ModuleSourceUrlCheck.Usable;
            }

            // Only ssh and scp-style urls may carry a bare git user; in any
            // other a user name is userinfo like the rest.
            var scheme = Scheme().Match(trimmed);
            var withoutGitUser = !scheme.Success
                ? StripLeadingGitUser(trimmed)
                : scheme.Groups["scheme"].Value.Equals("ssh", StringComparison.OrdinalIgnoreCase)
                    ? StripLeadingGitUser(trimmed[scheme.Length..])
                    : trimmed[scheme.Length..];

            return withoutGitUser.Contains('@') ? ModuleSourceUrlCheck.Credentials : ModuleSourceUrlCheck.Unusable;
        }

        /// <summary>
        /// Throws <see cref="ModuleSourceRefusedException"/> unless
        /// <see cref="Check"/> passes <paramref name="url"/>. The url itself is
        /// never part of the message.
        /// </summary>
        public static void EnsureUsable(string url)
        {
            switch (Check(url))
            {
                case ModuleSourceUrlCheck.Credentials:
                    throw new ModuleSourceRefusedException(CredentialsMessage);
                case ModuleSourceUrlCheck.Unusable:
                    throw new ModuleSourceRefusedException(UnusableMessage);
            }
        }

        private static int SchemeLength(string url) => Scheme().Match(url) is { Success: true } m ? m.Length : 0;

        private static string StripLeadingGitUser(string value) =>
            value.StartsWith(KeptUserName + "@", StringComparison.Ordinal) ? value[(KeptUserName.Length + 1)..] : value;

        /// <summary>A safe-to-show form of <paramref name="url"/>, or null if it is not in an allowed form.</summary>
        public static string Sanitize(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            url = url.Trim();

            if (HasWhitespaceOrControl(url))
            {
                return null;
            }

            var scheme = Scheme().Match(url);

            if (!scheme.Success)
            {
                return IsLocalPath(url) ? Local(url) : Scp(url);
            }

            var rest = url[scheme.Length..];

            return scheme.Groups["scheme"].Value.ToLowerInvariant() switch
            {
                "http" or "https" => Network(scheme.Groups["scheme"].Value.ToLowerInvariant(), rest, keepGit: false),
                "ssh" => Network("ssh", rest, keepGit: true),
                "file" => Local(url, rest),
                _ => null,
            };
        }

        private static string Network(string scheme, string rest, bool keepGit)
        {
            var parts = ParseNetwork(rest);

            if (parts == null)
            {
                return null;
            }

            var (userInfo, hostPort, path) = parts.Value;
            var user = keepGit && userInfo == KeptUserName ? KeptUserName + "@" : string.Empty;

            return $"{scheme}://{user}{hostPort}{path}";
        }

        /// <summary>
        /// Splits what follows <c>scheme://</c> into userinfo, host[:port] and
        /// path. The authority ends at the first <c>/</c>, <c>?</c> or <c>#</c>;
        /// within it the userinfo ends at the last <c>@</c>. Null unless the
        /// host and port are well formed and nothing after the authority holds
        /// an <c>@</c> or a <c>://</c>.
        /// </summary>
        private static (string UserInfo, string HostPort, string Path)? ParseNetwork(string rest)
        {
            var authorityEnd = rest.IndexOfAny(['/', '?', '#']);
            var authority = authorityEnd < 0 ? rest : rest[..authorityEnd];
            var remainder = authorityEnd < 0 ? string.Empty : rest[authorityEnd..];

            if (remainder.Contains('@') || remainder.Contains("://", StringComparison.Ordinal))
            {
                return null;
            }

            var at = authority.LastIndexOf('@');
            var userInfo = at < 0 ? null : authority[..at];
            var hostPort = at < 0 ? authority : authority[(at + 1)..];

            if (!HostPort().IsMatch(hostPort))
            {
                return null;
            }

            var cut = remainder.IndexOfAny(['?', '#']);
            var path = cut < 0 ? remainder : remainder[..cut];

            return (userInfo, hostPort, path);
        }

        /// <summary>
        /// scp-style <c>[user@]host:path</c>. The user, which may hold any
        /// character, ends at the last <c>@</c>; what follows must be a host,
        /// a <c>:</c> and a path with no <c>://</c>; a query or fragment is dropped.
        /// </summary>
        private static string Scp(string url)
        {
            var at = url.LastIndexOf('@');
            var userInfo = at < 0 ? null : url[..at];
            var address = at < 0 ? url : url[(at + 1)..];

            // Query and fragment go, as in the other forms.
            var cut = address.IndexOfAny(['?', '#']);
            address = cut < 0 ? address : address[..cut];

            var match = ScpAddress().Match(address);

            if (!match.Success || address.Contains("://", StringComparison.Ordinal))
            {
                return null;
            }

            return (userInfo == KeptUserName ? KeptUserName + "@" : string.Empty) + address;
        }

        /// <summary>A local path or <c>file://</c> url, as configured, if it holds no <c>@</c> and no further <c>://</c>.</summary>
        private static string Local(string url, string afterScheme = null)
        {
            var checkedPart = afterScheme ?? url;

            return checkedPart.Contains('@') || checkedPart.Contains("://", StringComparison.Ordinal) ? null : url;
        }

        /// <summary>An absolute, relative or home path, or a Windows one: what git clones from disk.</summary>
        private static bool IsLocalPath(string url) =>
            url.StartsWith('/') || url.StartsWith('.') || url.StartsWith('~') || url.Contains('\\') ||
            (url.Length > 2 && char.IsAsciiLetter(url[0]) && url[1] == ':' && url[2] is '/' or '\\');

        private static bool HasWhitespaceOrControl(string url)
        {
            foreach (var c in url)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Only a scheme at the very start counts.</summary>
        [GeneratedRegex(@"^(?<scheme>[A-Za-z][A-Za-z0-9+.\-]*)://")]
        private static partial Regex Scheme();

        /// <summary>A dns name or address, or a bracketed IPv6 address, with an optional numeric port.</summary>
        [GeneratedRegex(@"^(?:[A-Za-z0-9.\-]+|\[[0-9A-Fa-f:.]+\])(?::[0-9]{1,5})?$")]
        private static partial Regex HostPort();

        /// <summary>scp-style host:path: a host with no port, a colon, and a path without @.</summary>
        [GeneratedRegex(@"^(?:[A-Za-z0-9.\-]+|\[[0-9A-Fa-f:.]+\]):[^@]+$")]
        private static partial Regex ScpAddress();
    }
}
