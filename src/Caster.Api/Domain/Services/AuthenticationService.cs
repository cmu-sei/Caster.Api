// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Infrastructure.Options;
using IdentityModel.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Caster.Api.Domain.Services
{
    public interface IAuthenticationService
    {
        TokenResponse GetToken(CancellationToken ct = new CancellationToken());
        void InvalidateToken();
    }

    public class AuthenticationService : IAuthenticationService
    {
        private readonly Object _lock = new Object();
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IOptionsMonitor<ClientOptions> _clientOptions;
        private readonly ILogger<AuthenticationService> _logger;

        private TokenResponse _tokenResponse;

        public AuthenticationService(IHttpClientFactory httpClientFactory, IOptionsMonitor<ClientOptions> clientOptions, ILogger<AuthenticationService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _clientOptions = clientOptions;
            _logger = logger;
        }

        public TokenResponse GetToken(CancellationToken ct = new CancellationToken())
        {
            if (!ValidateToken())
            {
                lock (_lock)
                {
                    // Check again so we don't renew again if 
                    // another thread already did while we were waiting on the lock
                    if (!ValidateToken())
                    {
                        _tokenResponse = RenewToken(ct);
                    }                                        
                }
            }

            return _tokenResponse;
        }

        public void InvalidateToken()
        {
            _tokenResponse = null;
        }

        private bool ValidateToken()
        {
            if (_tokenResponse == null || _tokenResponse.ExpiresIn <= _clientOptions.CurrentValue.TokenRefreshSeconds)
            {
                return false;
            }
            else
            {
                return true;
            }
        }        

        private TokenResponse RenewToken(CancellationToken ct)
        {
            try
            {
                var clientOptions = _clientOptions.CurrentValue;
                var httpClient = _httpClientFactory.CreateClient("identity");
                var response = httpClient.RequestPasswordTokenAsync(new PasswordTokenRequest
                {
                    Address = clientOptions.TokenUrl,
                    ClientId = clientOptions.ClientId,
                    Scope = clientOptions.Scope,
                    UserName = clientOptions.UserName,
                    Password = clientOptions.Password
                }, ct).Result;

                return response;
            }
            catch(Exception ex)
            {
                _logger.LogError("Exception renewing auth token.", ex);
            }            

            return null;
        }
    }
}
