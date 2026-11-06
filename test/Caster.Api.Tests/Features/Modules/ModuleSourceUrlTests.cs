// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using Caster.Api.Domain.Services.Modules;
using Xunit;

namespace Caster.Api.Tests.Features.Modules
{
    [Trait("Category", "Unit")]
    [Trait("Category", "ModuleSources")]
    public class ModuleSourceUrlTests
    {
        private const string Secret = "SECRETVALUE";

        /// <summary>Every leak case found so far, and what is shown instead: a credential-free url, or nothing.</summary>
        [Theory]
        // https: userinfo of any shape is dropped, query and fragment too.
        [InlineData("https://git-access-token:SECRETVALUE@git.example.com/afp/afp-proxmox.git", "https://git.example.com/afp/afp-proxmox.git")]
        [InlineData("https://SECRETVALUE@git.example.com/afp/afp-proxmox.git", "https://git.example.com/afp/afp-proxmox.git")]
        [InlineData("https://git:SECRETVALUE@git.example.com/afp/afp-proxmox.git", "https://git.example.com/afp/afp-proxmox.git")]
        [InlineData("https://git@git.example.com/afp/afp-proxmox.git", "https://git.example.com/afp/afp-proxmox.git")]
        [InlineData("https://user:SECRET@VALUE@git.example.com/repo.git", "https://git.example.com/repo.git")]
        [InlineData("https://git.example.com/repo.git?private_token=SECRETVALUE#readme", "https://git.example.com/repo.git")]
        [InlineData("HTTPS://git.example.com/repo.git", "https://git.example.com/repo.git")]
        // A credential holding / ? # or :// leaves an @ after the parsed authority, or a bad port: not shown.
        [InlineData("https://user:p#SECRETVALUE@git.example.com/repo.git", null)]
        [InlineData("https://user:123?SECRETVALUE@git.example.com/repo.git", null)]
        [InlineData("https://user:123#SECRETVALUE@git.example.com/repo.git", null)]
        [InlineData("https://user:a/SECRETVALUE@git.example.com/repo.git", null)]
        [InlineData("https://user:12/SECRETVALUE@git.example.com/repo.git", null)]
        [InlineData("https://user:a://SECRETVALUE@git.example.com/repo.git", null)]
        [InlineData("https://git.example.com/repo.git?next=https://SECRETVALUE", null)]
        [InlineData("https://git.example.com/repo.git#x@SECRETVALUE", null)]
        // Ports and IPv6.
        [InlineData("https://git.example.com:8443/repo.git", "https://git.example.com:8443/repo.git")]
        [InlineData("https://user:SECRETVALUE@git.example.com:8443/repo.git?x=1", "https://git.example.com:8443/repo.git")]
        [InlineData("https://[2001:db8::1]:8443/repo.git", "https://[2001:db8::1]:8443/repo.git")]
        [InlineData("https://user:SECRETVALUE@[2001:db8::1]/repo.git", "https://[2001:db8::1]/repo.git")]
        [InlineData("https://git.example.com:SECRETVALUE/repo.git", null)]
        // ssh: a bare git user is kept, anything else dropped.
        [InlineData("ssh://git@git.example.com/afp/afp-proxmox.git", "ssh://git@git.example.com/afp/afp-proxmox.git")]
        [InlineData("ssh://git.example.com:2222/afp/afp-proxmox.git", "ssh://git.example.com:2222/afp/afp-proxmox.git")]
        [InlineData("ssh://git:SECRETVALUE@git.example.com/repo.git", "ssh://git.example.com/repo.git")]
        [InlineData("ssh://ghp_SECRETVALUE@github.com/org/repo.git", "ssh://github.com/org/repo.git")]
        [InlineData("ssh://u:a/SECRETVALUE@github.com/org/repo.git", null)]
        // scp-style.
        [InlineData("git@git.example.com:afp/afp-proxmox.git", "git@git.example.com:afp/afp-proxmox.git")]
        [InlineData("git.example.com:afp/afp-proxmox.git", "git.example.com:afp/afp-proxmox.git")]
        [InlineData("ghp_SECRETVALUE@github.com:org/repo.git", "github.com:org/repo.git")]
        [InlineData("git:SECRETVALUE@github.com:org/repo.git", "github.com:org/repo.git")]
        [InlineData("oauth2:SECRETVALUE@gitlab.example.com:group/repo.git", "gitlab.example.com:group/repo.git")]
        [InlineData("oauth2:abc/SECRETVALUE@gitlab.example.com:group/repo.git", "gitlab.example.com:group/repo.git")]
        [InlineData("oauth2:a:b/c?d#SECRETVALUE@gitlab.example.com:group/repo.git", "gitlab.example.com:group/repo.git")]
        [InlineData("x:y@SECRETVALUE@gitlab.example.com:group/repo.git", "gitlab.example.com:group/repo.git")]
        [InlineData("oauth2:x://SECRETVALUE@gitlab.example.com:group/repo.git", "gitlab.example.com:group/repo.git")]
        [InlineData("oauth2:SECRETVALUE@gitlab.example.com/group/repo.git", null)]
        [InlineData("git@gitlab.example.com:group/repo.git?private_token=SECRETVALUE#x", "git@gitlab.example.com:group/repo.git")]
        [InlineData("gitlab.example.com:group/repo.git#SECRETVALUE", "gitlab.example.com:group/repo.git")]
        [InlineData("gitlab.example.com:?SECRETVALUE", null)]
        // Local.
        [InlineData("file:///srv/git/catalog.git", "file:///srv/git/catalog.git")]
        [InlineData("/srv/git/catalog.git", "/srv/git/catalog.git")]
        [InlineData("./repos/catalog.git", "./repos/catalog.git")]
        [InlineData("C:/repos/catalog.git", "C:/repos/catalog.git")]
        [InlineData("/srv/git/user@host/catalog.git", null)]
        [InlineData("file:///srv/git/SECRET@VALUE.git", null)]
        [InlineData("file://user:SECRETVALUE@host/srv/git/catalog.git", null)]
        // Anything else.
        [InlineData("ftp://git.example.com/repo.git", null)]
        [InlineData(" x https://SECRETVALUE@host/repo.git", null)]
        [InlineData("not a url", null)]
        public void Sanitize_ShowsOnlyAllowedForms(string configured, string expected)
        {
            var url = ModuleSourceUrl.Sanitize(configured);

            Assert.Equal(expected, url);
            Assert.DoesNotContain("SECRET", url ?? string.Empty);
        }

        [Theory]
        // Usable: no secret, or a user name where it names an account.
        [InlineData("https://git.example.com/org/repo.git", ModuleSourceUrlCheck.Usable)]
        [InlineData("HTTPS://git.example.com/org/repo.git", ModuleSourceUrlCheck.Usable)]
        [InlineData("https://[2001:db8::1]:8443/org/repo.git", ModuleSourceUrlCheck.Usable)]
        [InlineData("ssh://git@git.example.com:2222/org/repo.git", ModuleSourceUrlCheck.Usable)]
        [InlineData("ssh://deploy@git.example.com/org/repo.git", ModuleSourceUrlCheck.Usable)]
        [InlineData("git+ssh://deploy@git.example.com/org/repo.git", ModuleSourceUrlCheck.Usable)]
        [InlineData("git@git.example.com:org/repo.git", ModuleSourceUrlCheck.Usable)]
        [InlineData("deploy@git.example.com:org/repo.git", ModuleSourceUrlCheck.Usable)]
        [InlineData("git@ssh.dev.azure.com:v3/org/project/repo", ModuleSourceUrlCheck.Usable)]
        [InlineData("git.example.com:org/repo.git", ModuleSourceUrlCheck.Usable)]
        [InlineData("git://git.example.com/org/repo.git", ModuleSourceUrlCheck.Usable)]
        [InlineData("file:///srv/git/repo.git", ModuleSourceUrlCheck.Usable)]
        [InlineData("/srv/git/repo.git", ModuleSourceUrlCheck.Usable)]
        [InlineData("./repos/catalog.git", ModuleSourceUrlCheck.Usable)]
        [InlineData("repos/catalog.git", ModuleSourceUrlCheck.Usable)]
        [InlineData("/srv/git/user@host/catalog.git", ModuleSourceUrlCheck.Usable)]
        // Credentials: a password anywhere, or any user in http(s).
        [InlineData("https://oauth2:SECRET@git.example.com/org/repo.git", ModuleSourceUrlCheck.Credentials)]
        [InlineData("https://git@git.example.com/org/repo.git", ModuleSourceUrlCheck.Credentials)]
        [InlineData("https://ghp_SECRET@github.com/org/repo.git", ModuleSourceUrlCheck.Credentials)]
        [InlineData("https://org@dev.azure.com/org/project/_git/repo", ModuleSourceUrlCheck.Credentials)]
        [InlineData("https://user:p#ss@git.example.com/org/repo.git", ModuleSourceUrlCheck.Credentials)]
        [InlineData("https://user:123?SECRET@git.example.com/org/repo.git", ModuleSourceUrlCheck.Credentials)]
        [InlineData("https://git.example.com/org/repo.git#x@SECRET", ModuleSourceUrlCheck.Credentials)]
        [InlineData("ssh://git:SECRET@github.com/org/repo.git", ModuleSourceUrlCheck.Credentials)]
        [InlineData("ssh://u:a/SECRET@github.com/org/repo.git", ModuleSourceUrlCheck.Credentials)]
        [InlineData("git://user:SECRET@git.example.com/org/repo.git", ModuleSourceUrlCheck.Credentials)]
        [InlineData("git://SECRET@git.example.com/org/repo.git", ModuleSourceUrlCheck.Credentials)]
        [InlineData("oauth2:abc/def@gitlab.example.com/group/repo.git", ModuleSourceUrlCheck.Credentials)]
        [InlineData("oauth2:SECRET@gitlab.example.com:group/repo.git", ModuleSourceUrlCheck.Credentials)]
        [InlineData("u:p@ss@gitlab.example.com:group/repo.git", ModuleSourceUrlCheck.Credentials)]
        [InlineData("file://user:SECRET@host/srv/git/repo.git", ModuleSourceUrlCheck.Credentials)]
        [InlineData("ftp://user:SECRET@git.example.com/org/repo.git", ModuleSourceUrlCheck.Credentials)]
        // Unusable.
        [InlineData("https://git.example.com/org/repo.git?private_token=x", ModuleSourceUrlCheck.Unusable)]
        [InlineData("git@git.example.com:org/repo.git?private_token=x", ModuleSourceUrlCheck.Unusable)]
        [InlineData("https://git.example.com:SECRET/org/repo.git", ModuleSourceUrlCheck.Unusable)]
        [InlineData("ftp://git.example.com/org/repo.git", ModuleSourceUrlCheck.Unusable)]
        [InlineData("not a url", ModuleSourceUrlCheck.Unusable)]
        [InlineData("", ModuleSourceUrlCheck.Unusable)]
        public void Check_RefusesPasswordsAndHttpUsers(string url, ModuleSourceUrlCheck expected)
        {
            Assert.Equal(expected, ModuleSourceUrl.Check(url));
        }

        /// <summary>Whatever Check lets through is never shown with a password, whatever Sanitize does with it.</summary>
        [Theory]
        [MemberData(nameof(CredentialShapedSecrets))]
        public void Check_NeverPassesAPasswordSplicedIntoTheUserOrPassword(string secret)
        {
            foreach (var configured in new[]
            {
                $"https://user:{secret}git.example.com/repo.git",
                $"https://{secret}git.example.com/repo.git",
                $"ssh://git:{secret}git.example.com/repo.git",
                $"git://u:{secret}git.example.com/repo.git",
                $"user:{secret}git.example.com:org/repo.git",
                $"file://user:{secret}host/srv/repo.git",
            })
            {
                Assert.NotEqual(ModuleSourceUrlCheck.Usable, ModuleSourceUrl.Check(configured));
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public void Sanitize_BlankIsNull(string configured)
        {
            Assert.Null(ModuleSourceUrl.Sanitize(configured));
        }

        public static IEnumerable<object[]> CredentialShapedSecrets()
        {
            // Each ends in @, which is what makes text a url's userinfo.
            foreach (var secret in new[]
            {
                $"{Secret}@", $"user:{Secret}@", $"{Secret}:x@", $"a#{Secret}@", $"a?{Secret}@", $"a/{Secret}@",
                $"a://{Secret}@", $"1234{Secret}@", $"a:{Secret}#b@", $"x@{Secret}@", $"a:b/c?d#e{Secret}f://g@",
            })
            {
                yield return [secret];
            }
        }

        private static readonly string[] Bases =
        [
            "https://git.example.com:8443/org/repo.git?x=1#y",
            "http://[2001:db8::1]/org/repo.git",
            "ssh://git@git.example.com:2222/org/repo.git",
            "git@git.example.com:org/repo.git",
            "git.example.com:org/repo.git",
            "file:///srv/git/repo.git",
            "/srv/git/repo.git",
        ];

        /// <summary>A credential-shaped secret spliced in at every position of every allowed form is never shown.</summary>
        [Theory]
        [MemberData(nameof(CredentialShapedSecrets))]
        public void Sanitize_NeverShowsASecretSplicedInAnywhere(string secret)
        {
            foreach (var url in Bases)
            {
                for (var i = 0; i <= url.Length; i++)
                {
                    var configured = url[..i] + secret + url[i..];
                    var shown = ModuleSourceUrl.Sanitize(configured) ?? string.Empty;

                    Assert.False(shown.Contains(Secret), $"'{configured}' was shown as '{shown}'.");
                }
            }
        }

        /// <summary>A secret with no @, put where a user or password goes, is never shown.</summary>
        [Theory]
        [InlineData(Secret)]
        [InlineData("a#" + Secret)]
        [InlineData("a?" + Secret)]
        [InlineData("a/" + Secret)]
        [InlineData("a://" + Secret)]
        [InlineData("1234" + Secret)]
        [InlineData(Secret + ":x")]
        public void Sanitize_NeverShowsASecretInTheUserOrPassword(string secret)
        {
            foreach (var configured in new[]
            {
                $"https://user:{secret}@git.example.com/repo.git",
                $"https://{secret}@git.example.com/repo.git",
                $"ssh://git:{secret}@git.example.com/repo.git",
                $"ssh://{secret}@git.example.com/repo.git",
                $"user:{secret}@git.example.com:org/repo.git",
                $"{secret}@git.example.com:org/repo.git",
                $"file://user:{secret}@host/srv/repo.git",
            })
            {
                var shown = ModuleSourceUrl.Sanitize(configured) ?? string.Empty;

                Assert.False(shown.Contains(Secret), $"'{configured}' was shown as '{shown}'.");
            }
        }

        [Theory]
        [InlineData("https://gitlab.example.com/api/v4/", "https://gitlab.example.com/")]
        [InlineData("https://gitlab.example.com:8443/api/v4/?private_token=SECRETVALUE", "https://gitlab.example.com:8443/")]
        [InlineData("https://user:SECRETVALUE@gitlab.example.com:8443/api/v4/", null)]
        [InlineData("https://user:123?SECRETVALUE@gitlab.example.com/api/v4/", null)]
        [InlineData("https://user:a/SECRETVALUE@gitlab.example.com/api/v4/", null)]
        [InlineData("ssh://gitlab.example.com/", null)]
        [InlineData("not a url", null)]
        public void GitlabBase_IsAnAllowedServerUrlOrNull(string apiUrl, string expected)
        {
            Assert.Equal(expected, ModuleSourceUrl.GitlabBase(apiUrl));
        }
    }
}
