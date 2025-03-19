// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Options;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Text.RegularExpressions;

namespace Caster.Api.Domain.Services
{
    public interface IUserClaimsService
    {
        Task<ClaimsPrincipal> AddUserClaims(ClaimsPrincipal principal, bool update);
        Task<ClaimsPrincipal> GetClaimsPrincipal(Guid userId, bool setAsCurrent);
        Task<ClaimsPrincipal> RefreshClaims(Guid userId);
        ClaimsPrincipal GetCurrentClaimsPrincipal();
        void SetCurrentClaimsPrincipal(ClaimsPrincipal principal);
    }

    public class UserClaimsService : IUserClaimsService
    {
        private readonly CasterContext _context;
        private readonly ClaimsTransformationOptions _options;
        private IMemoryCache _cache;
        private ClaimsPrincipal _currentClaimsPrincipal;

        public UserClaimsService(CasterContext context, IMemoryCache cache, ClaimsTransformationOptions options)
        {
            _context = context;
            _options = options;
            _cache = cache;
        }

        public async Task<ClaimsPrincipal> AddUserClaims(ClaimsPrincipal principal, bool update)
        {
            List<Claim> claims;
            var identity = (ClaimsIdentity)principal.Identity;
            var userId = principal.GetId();

            // Don't use cached claims if given a new token and we are using roles or groups from the token
            if (_cache.TryGetValue(userId, out claims) && (_options.UseGroupsFromIdP || _options.UseRolesFromIdP))
            {
                var cachedTokenId = claims.FirstOrDefault(x => x.Type == JwtRegisteredClaimNames.Jti)?.Value;
                var newTokenId = identity.Claims.FirstOrDefault(x => x.Type == JwtRegisteredClaimNames.Jti)?.Value;

                if (newTokenId != cachedTokenId)
                {
                    claims = null;
                }
            }

            if (claims == null)
            {
                claims = [];
                var user = await ValidateUser(userId, principal.FindFirst("name")?.Value, update);

                if (user != null)
                {
                    var jtiClaim = identity.Claims.Where(x => x.Type == JwtRegisteredClaimNames.Jti).FirstOrDefault();

                    if (jtiClaim is not null)
                    {
                        claims.Add(new Claim(jtiClaim.Type, jtiClaim.Value));
                    }

                    claims.AddRange(await GetPermissionClaims(userId, principal));

                    if (_options.EnableCaching)
                    {
                        _cache.Set(userId, claims, new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromSeconds(_options.CacheExpirationSeconds)));
                    }
                }
            }

            addNewClaims(identity, claims);
            return principal;
        }

        public async Task<ClaimsPrincipal> GetClaimsPrincipal(Guid userId, bool setAsCurrent)
        {
            ClaimsIdentity identity = new ClaimsIdentity();
            identity.AddClaim(new Claim("sub", userId.ToString()));
            ClaimsPrincipal principal = new ClaimsPrincipal(identity);

            principal = await AddUserClaims(principal, false);

            if (setAsCurrent || _currentClaimsPrincipal.GetId() == userId)
            {
                _currentClaimsPrincipal = principal;
            }

            return principal;
        }

        public async Task<ClaimsPrincipal> RefreshClaims(Guid userId)
        {
            _cache.Remove(userId);
            return await GetClaimsPrincipal(userId, false);
        }

        public ClaimsPrincipal GetCurrentClaimsPrincipal()
        {
            return _currentClaimsPrincipal;
        }

        public void SetCurrentClaimsPrincipal(ClaimsPrincipal principal)
        {
            _currentClaimsPrincipal = principal;
        }

        private async Task<User> ValidateUser(Guid subClaim, string nameClaim, bool update)
        {
            var user = await _context.Users
                .Where(u => u.Id == subClaim)
                .FirstOrDefaultAsync();

            var anyUsers = await _context.Users.AnyAsync();

            if (update)
            {
                if (user == null)
                {
                    user = new User
                    {
                        Id = subClaim,
                        Name = nameClaim ?? "Anonymous"
                    };

                    _context.Users.Add(user);
                }
                else
                {
                    if (nameClaim != null && user.Name != nameClaim)
                    {
                        user.Name = nameClaim;
                        _context.Update(user);
                    }
                }

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (Exception) { }
            }

            return user;
        }

        private async Task<IEnumerable<Claim>> GetPermissionClaims(Guid userId, ClaimsPrincipal principal)
        {
            List<Claim> claims = new();

            var tokenRoleNames = _options.UseRolesFromIdP ?
                this.GetClaimsFromToken(principal, _options.RolesClaimPath).Select(x => x.ToLower()) :
                [];

            var roles = await _context.SystemRoles
                .Where(x => tokenRoleNames.Contains(x.Name.ToLower()))
                .ToListAsync();

            var userRole = await _context.Users
                .Where(x => x.Id == userId)
                .Select(x => x.Role)
                .FirstOrDefaultAsync();

            if (userRole != null)
            {
                roles.Add(userRole);
            }

            roles = roles.Distinct().ToList();

            foreach (var role in roles)
            {
                List<string> permissions;

                if (role.AllPermissions)
                {
                    permissions = Enum.GetValues<SystemPermission>().Select(x => x.ToString()).ToList();
                }
                else
                {
                    permissions = role.Permissions.Select(x => x.ToString()).ToList();
                }

                foreach (var permission in permissions)
                {
                    if (!claims.Any(x => x.Type == AuthorizationConstants.PermissionsClaimType &&
                        x.Value == permission))
                    {
                        claims.Add(new Claim(AuthorizationConstants.PermissionsClaimType, permission));
                    }
                    ;
                }
            }

            var groupNames = _options.UseGroupsFromIdP ?
                this.GetClaimsFromToken(principal, _options.GroupsClaimPath).Select(x => x.ToLower()) :
                [];

            var groupIds = await _context.Groups
                .Where(x => x.Memberships.Any(y => y.UserId == userId) || groupNames.Contains(x.Name.ToLower()))
                .Select(x => x.Id)
                .ToListAsync();

            // Get Project Permissions
            var projectMemberships = await _context.ProjectMemberships
                .Where(x => x.UserId == userId || (x.GroupId.HasValue && groupIds.Contains(x.GroupId.Value)))
                .Include(x => x.Role)
                .GroupBy(x => x.ProjectId)
                .ToListAsync();

            foreach (var group in projectMemberships)
            {
                var projectPermissions = new List<ProjectPermission>();

                foreach (var membership in group)
                {
                    if (membership.Role.AllPermissions)
                    {
                        projectPermissions.AddRange(Enum.GetValues<ProjectPermission>());
                    }
                    else
                    {
                        projectPermissions.AddRange(membership.Role.Permissions);
                    }
                }

                var permissionsClaim = new ProjectPermissionsClaim
                {
                    ProjectId = group.Key,
                    Permissions = projectPermissions.Distinct().ToArray()
                };

                claims.Add(new Claim(AuthorizationConstants.ProjectPermissionsClaimType, permissionsClaim.ToString()));
            }

            return claims;
        }

        private string[] GetClaimsFromToken(ClaimsPrincipal principal, string claimPath)
        {
            if (string.IsNullOrEmpty(claimPath))
            {
                return [];
            }

            // Name of the claim to insert into the token. This can be a fully qualified name like 'address.street'.
            // In this case, a nested json object will be created. To prevent nesting and use dot literally, escape the dot with backslash (\.).
            var pathSegments = Regex.Split(claimPath, @"(?<!\\)\.").Select(s => s.Replace("\\.", ".")).ToArray();

            var tokenClaim = principal.Claims.Where(x => x.Type == pathSegments.First()).FirstOrDefault();

            if (tokenClaim == null)
            {
                return [];
            }

            return tokenClaim.ValueType switch
            {
                ClaimValueTypes.String => [tokenClaim.Value],
                JsonClaimValueTypes.Json => ExtractJsonClaimValues(tokenClaim.Value, pathSegments.Skip(1)),
                _ => []
            };
        }

        private string[] ExtractJsonClaimValues(string json, IEnumerable<string> pathSegments)
        {
            List<string> values = new();
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement currentElement = doc.RootElement;

                foreach (var segment in pathSegments)
                {
                    if (!currentElement.TryGetProperty(segment, out JsonElement propertyElement))
                    {
                        return [];
                    }

                    currentElement = propertyElement;
                }

                if (currentElement.ValueKind == JsonValueKind.Array)
                {
                    values.AddRange(currentElement.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString()));
                }
                else if (currentElement.ValueKind == JsonValueKind.String)
                {
                    values.Add(currentElement.GetString());
                }
            }
            catch (JsonException)
            {
                // Handle invalid JSON format
            }

            return values.ToArray();
        }

        private void addNewClaims(ClaimsIdentity identity, List<Claim> claims)
        {
            var newClaims = new List<Claim>();
            claims.ForEach(delegate (Claim claim)
            {
                if (!identity.Claims.Any(identityClaim => identityClaim.Type == claim.Type))
                {
                    newClaims.Add(claim);
                }
            });
            identity.AddClaims(newClaims);
        }
    }
}
