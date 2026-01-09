// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Options;
using Docker.Registry.DotNet;
using Docker.Registry.DotNet.Domain.Registry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Caster.Api.Domain.Services.Terraform;

public interface IImageTagService
{
    IEnumerable<string> GetTags();
}

public class ImageTagService : BackgroundService, IImageTagService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ImageTagService> _logger;

    private KubernetesJobOptions _options;
    private string[] _tags = [];
    private TaskCompletionSource<bool> _taskCompletionSource = new TaskCompletionSource<bool>();
    private RegistryClientConfiguration _registryClientConfiguration;
    private IRegistryClient _client;
    private Regex _regex;
    private string _regexPattern;

    public ImageTagService(IOptionsMonitor<TerraformOptions> optionsMonitor, ILogger<ImageTagService> logger, IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _options = optionsMonitor.CurrentValue.KubernetesJobs;
        _logger = logger;

        optionsMonitor.OnChange(x =>
        {
            _options = optionsMonitor.CurrentValue.KubernetesJobs;
            this.ForceQuery();
        });
    }

    public IEnumerable<string> GetTags()
    {
        return _tags;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await DoWork(_options, cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Wait(_options, stoppingToken);
            await DoWork(_options, stoppingToken);
        }
    }

    public void ForceQuery()
    {
        _taskCompletionSource.TrySetCanceled();
    }

    private async Task DoWork(KubernetesJobOptions options, CancellationToken cancellationToken)
    {
        try
        {
            if (options.Enabled)
            {
                if (options.QueryImageTags)
                {
                    _logger.LogInformation("Querying image tags...");
                    _tags = await QueryTags(options, cancellationToken);
                }
                else
                {
                    _logger.LogInformation("QueryImageTags disabled, skipping");
                    _tags = options.ImageTags;
                }
            }
            else
            {
                _logger.LogInformation("Kubernetes Jobs disabled, skipping query image tags check");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception getting image tags.");
        }
    }

    private async Task Wait(KubernetesJobOptions options, CancellationToken cancellationToken)
    {
        await Task.WhenAny(Task.Delay(TimeSpan.FromMinutes(options.QueryImageTagsMinutes), cancellationToken), _taskCompletionSource.Task);

        if (_taskCompletionSource.Task.IsCanceled)
        {
            _taskCompletionSource = new TaskCompletionSource<bool>();
        }
    }

    private async Task<string[]> QueryTags(KubernetesJobOptions options, CancellationToken cancellationToken)
    {
        string[] tagValues = null;

        try
        {
            _logger.LogInformation("Querying for image tags for {imageName} in {imageRegistry}", options.ImageName, options.ImageRegistry);
            var client = GetClient(options);
            var tags = await client.Tags.ListTags(options.ImageName, token: cancellationToken);
            tagValues = tags.Tags.Select(x => x.Value).ToArray();
            _logger.LogInformation("Found {tagCount} tags", tagValues.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception querying image tags, using previous values");
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CasterContext>();
        var dbContainerImage = await db.ContainerImages.Where(x => x.Name == options.ImageName).FirstOrDefaultAsync(cancellationToken);

        // if query succeeded, update the tags in the db. Otherwise, just return what's already in the db, if anything.
        if (tagValues is not null)
        {
            if (dbContainerImage is null)
            {
                dbContainerImage = new ContainerImage() { Name = options.ImageName };
                db.Add(dbContainerImage);
            }

            dbContainerImage.Tags = tagValues;
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            if (dbContainerImage is not null)
            {
                tagValues = dbContainerImage.Tags;
            }
            else
            {
                tagValues = [];
            }
        }

        if (!string.IsNullOrEmpty(options.QueryImageTagsRegex))
        {
            return FilterTags(tagValues, options).ToArray();
        }
        else
        {
            return tagValues;
        }
    }

    private IRegistryClient GetClient(KubernetesJobOptions options)
    {
        var imageRegistryUri = new Uri(options.ImageRegistry, UriKind.Absolute);

        if (_registryClientConfiguration is null || _registryClientConfiguration.BaseAddress != imageRegistryUri)
        {
            _registryClientConfiguration = new RegistryClientConfiguration(options.ImageRegistry);

            if (_client is not null)
            {
                _client.Dispose();
                _client = null;
            }
        }

        if (_client is null)
        {
            _client = _registryClientConfiguration.CreateClient();
        }

        return _client;
    }

    private IEnumerable<string> FilterTags(IEnumerable<string> tags, KubernetesJobOptions options)
    {
        if (string.IsNullOrEmpty(options.QueryImageTagsRegex))
            return tags;

        if (_regex is null || _regexPattern is not null && _regexPattern != options.QueryImageTagsRegex)
        {
            _regex = new Regex(options.QueryImageTagsRegex, RegexOptions.Compiled);
            _regexPattern = options.QueryImageTagsRegex;
        }

        return tags.Where(x => _regex.IsMatch(x));
    }
}
