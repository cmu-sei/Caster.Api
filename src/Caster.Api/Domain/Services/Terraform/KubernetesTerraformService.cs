// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Infrastructure.Utilities;
using k8s;
using k8s.Autorest;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace Caster.Api.Domain.Services.Terraform;

public class KubernetesTerraformService : BaseTerraformService
{
    private readonly ILogger<KubernetesTerraformService> _logger;
    private readonly IKubernetes _k8sClient;
    private readonly IImageTagService _imageTagService;
    private readonly BackoffTracker _backoffTracker = new(60);

    private const string _appName = "caster";
    private const string _appLabel = "app";
    private const string _workspaceIdLabel = "workspaceId";
    private const string _workspaceNameLabel = "workspaceName";
    private const string _hostVolumeName = "host-vol";

    public KubernetesTerraformService(TerraformOptions options, ILogger<KubernetesTerraformService> logger, IKubernetes k8sClient, IImageTagService imageTagService, IRegexService regexService) : base(options, regexService)
    {
        _logger = logger;
        _k8sClient = k8sClient;
        _imageTagService = imageTagService;
        _regexService = regexService;
    }

    protected override async Task<TerraformResult> Run(Workspace workspace,
                                                       IEnumerable<string> argumentList,
                                                       Action<string> outputHandler,
                                                       bool resume,
                                                       Func<string, Task> finalizer,
                                                       bool redirectStandardError = true)
    {
        int exitCode = -1;
        StringBuilder outputBuilder = new StringBuilder();

        var jobName = GetJobName(workspace);
        IKubernetes client = _k8sClient;

        // Check for existing Job
        V1Job job;
        var existingJob = await _k8sClient.BatchV1.ListNamespacedJobAsync(_options.KubernetesJobs.Namespace, labelSelector: $"{_appLabel}={_appName},{_workspaceIdLabel}={workspace.Id}");

        if (existingJob.Items.Count == 0)
        {
            if (resume)
            {
                return new TerraformResult()
                {
                    ExitCode = 0,
                    Output = string.Empty
                };
            }

            var securityContext = await GetSecurityContext();
            var (volumes, volumeMounts) = GetVolumes();
            var envVars = GetEnvironmentVariables();

            var jobRequest = new V1Job
            {
                Metadata = new V1ObjectMeta
                {
                    Name = jobName,
                    Annotations = new Dictionary<string, string>
                    {
                        { "cancellable", "true"}
                    },
                    Labels = new Dictionary<string, string>
                    {
                        { _appLabel, _appName},
                        { _workspaceIdLabel, workspace.Id.ToString() },
                        { _workspaceNameLabel, workspace.Name}
                    }
                },
                Spec = new V1JobSpec
                {
                    Template = new V1PodTemplateSpec
                    {
                        Spec = new V1PodSpec
                        {
                            Containers =
                            [
                                new V1Container
                                {
                                    Name = jobName,
                                    Image = GetImage(workspace),
                                    Args = argumentList?.ToArray(),
                                    WorkingDir = $"{_options.KubernetesJobs.RootWorkingDirectory}/{workspace.Id}",
                                    VolumeMounts = volumeMounts,
                                    Env = envVars
                                }
                            ],
                            Volumes = volumes,
                            SecurityContext = securityContext,
                            RestartPolicy = "Never"
                        }
                    },
                }
            };

            try
            {
                job = await client.BatchV1.CreateNamespacedJobAsync(jobRequest, _options.KubernetesJobs.Namespace);
                Console.WriteLine($"Job {jobName} created successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job Error");

                return new TerraformResult()
                {
                    ExitCode = -1,
                    Output = "Failed to create job"
                };
            }
        }
        else
        {
            job = existingJob.Items.SingleOrDefault()
                ?? throw new InvalidOperationException("No job found.");

            var container = job.Spec?.Template?.Spec?.Containers?.SingleOrDefault()
                ?? throw new InvalidOperationException("Job does not contain exactly one container.");

            var jobArg = container.Args?.FirstOrDefault();
            var expectedArg = argumentList?.FirstOrDefault();

            if (jobArg is not null && expectedArg is not null)
            {
                if (!string.Equals(jobArg, expectedArg, StringComparison.Ordinal))
                {
                    throw new TerraformCommandConflictException(jobArg);
                }
            }
        }

        try
        {
            var pod = await WaitForPodReady(job);

            if (pod == null)
            {
                return new TerraformResult { ExitCode = -1, Output = "Job no longer exists" };
            }

            await StreamJobLogs(pod, job, outputHandler);

            bool podDeleted = await CollectFinalLogs(pod, outputBuilder);

            bool finalizerComplete = false;
            _backoffTracker.Reset();

            while (!finalizerComplete)
            {
                try
                {
                    await (finalizer?.Invoke(outputBuilder.ToString()) ?? Task.CompletedTask);
                    finalizerComplete = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in finalizer for {jobName}, retrying...", job.Metadata.Name);
                    await _backoffTracker.WaitAsync();
                }
            }

            if (!podDeleted)
            {
                exitCode = await GetExitCode(pod);
            }

            await EnsureJobDeleted(job.Metadata.Name, job.Metadata.NamespaceProperty);

            _logger.LogInformation("Job confirmed deleted - {jobName}", jobName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical Job Error");
            throw;
        }

        return new TerraformResult
        {
            Output = outputBuilder.ToString(),
            ExitCode = exitCode
        };
    }

    private string GetImage(Workspace workspace)
    {
        var version = string.IsNullOrEmpty(workspace.TerraformVersion) ?
            _options.DefaultVersion :
            workspace.TerraformVersion;

        if (IsValidVersion(version))
        {
            return $"{_options.KubernetesJobs.ImageName}:{version}";
        }
        else
        {
            throw new ArgumentException("Unauthorized version specified.");
        }
    }

    public override bool IsValidVersion(string version)
    {
        return _imageTagService.GetTags().Contains(version);
    }

    public override IEnumerable<string> GetVersions()
    {
        return _imageTagService.GetTags();
    }

    public override async Task<TerraformResult> CancelRun(Workspace workspace, bool force)
    {
        var jobName = GetJobName(workspace);

        var job = await _k8sClient.BatchV1.ReadNamespacedJobAsync(jobName, _options.KubernetesJobs.Namespace);

        if (job == null)
        {
            _logger.LogDebug("Couldn't find job to cancel");
            return null;
        }
        else
        {
            var options = new V1DeleteOptions(propagationPolicy: "Foreground", gracePeriodSeconds: long.MaxValue);

            if (force)
            {
                options.GracePeriodSeconds = 0;
            }

            await _k8sClient.BatchV1.DeleteNamespacedJobAsync(
                            job.Metadata.Name,
                            job.Metadata.NamespaceProperty,
                            options);

            return new TerraformResult
            {
                ExitCode = 0,
                Output = ""
            };
        }
    }

    public override async Task<IEnumerable<Guid>> GetActiveWorkspaces()
    {
        var jobs = await _k8sClient.BatchV1.ListNamespacedJobAsync(_options.KubernetesJobs.Namespace, labelSelector: $"{_appLabel}={_appName}");

        var workspaceIds = jobs.Items
            .Where(x => x.Metadata?.Labels != null && x.Metadata.Labels.ContainsKey(_workspaceIdLabel))
            .Select(x => x.Metadata.Labels[_workspaceIdLabel])
            .Select(x => Guid.TryParse(x, out var guid) ? guid : (Guid?)null)
            .Where(x => x.HasValue)
            .Select(x => x.Value)
            .ToList();

        return workspaceIds;
    }

    protected override string GetBasePath(Workspace workspace) => workspace.GetPath(_options.KubernetesJobs.RootWorkingDirectory);

    private async Task<V1Pod> WaitForPodReady(V1Job job)
    {
        _backoffTracker.Reset();
        V1Pod pod = null;

        while (pod == null)
        {
            try
            {
                var tcs = new TaskCompletionSource<V1Pod>(TaskCreationOptions.RunContinuationsAsynchronously);

                using (var podResp = await _k8sClient.CoreV1.ListNamespacedPodWithHttpMessagesAsync(
                    job.Metadata.NamespaceProperty,
                    labelSelector: $"job-name={job.Metadata.Name}",
                    watch: true))
                {
                    using var watcher = podResp.Watch<V1Pod, V1PodList>(
                        onEvent: (type, p) =>
                        {
                            if (type == WatchEventType.Deleted ||
                                p.Status?.Phase is "Running" or "Succeeded" or "Failed")
                            {
                                tcs.TrySetResult(p);
                            }
                        },
                        onError: e => tcs.TrySetException(e),
                        onClosed: () => tcs.TrySetCanceled());

                    pod = await tcs.Task;
                }
            }
            catch (Exception ex)
            {
                if (await JobExists(job.Metadata.Name, job.Metadata.NamespaceProperty))
                {
                    _logger.LogWarning(ex, "Error watching for pod, retrying...");
                    await _backoffTracker.WaitAsync();
                }
                else
                {
                    return null;
                }
            }
        }

        _logger.LogDebug("Pod {podName} ready for logs", pod.Metadata.Name);
        return pod;
    }

    private async Task StreamJobLogs(V1Pod pod, V1Job job, Action<string> outputHandler)
    {
        _backoffTracker.Reset();
        var lastTimestamp = DateTime.UtcNow;
        var completed = false;

        while (!completed)
        {
            try
            {
                int seconds = Math.Max(0, (int)(DateTime.UtcNow - lastTimestamp).TotalSeconds);

                using var logStream = await _k8sClient.CoreV1.ReadNamespacedPodLogAsync(
                    pod.Metadata.Name,
                    job.Metadata.NamespaceProperty,
                    follow: true,
                    sinceSeconds: seconds > 0 ? seconds : null);

                using var reader = new StreamReader(logStream);
                string line;
                while (!reader.EndOfStream)
                {
                    line = await reader.ReadLineAsync();
                    if (line != null)
                    {
                        outputHandler?.Invoke(line);
                        lastTimestamp = DateTime.UtcNow;
                    }
                }

                // Wait for job to finish
                // If no events by cancellation token timeout, job was probably already deleted, so re-check status
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var listJobTask = _k8sClient.BatchV1.ListNamespacedJobWithHttpMessagesAsync(
                    job.Metadata.NamespaceProperty,
                    fieldSelector: $"metadata.name={job.Metadata.Name}",
                    watch: true,
                    cancellationToken: cts.Token);

                await foreach (var (type, item) in listJobTask.WatchAsync<V1Job, V1JobList>(cancellationToken: cts.Token))
                {
                    if (item.Status?.Succeeded > 0)
                    {
                        _logger.LogDebug("Job {jobName} completed successfully", job.Metadata.Name);
                        completed = true;
                        break;
                    }
                    else if (item.Status?.Failed > 0)
                    {
                        _logger.LogDebug("Job {jobName} failed", job.Metadata.Name);
                        completed = true;
                        break;
                    }
                    else if (type == WatchEventType.Deleted)
                    {
                        _logger.LogDebug("Job {jobName} was deleted", job.Metadata.Name);
                        completed = true;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception reading logs, checking job status");

                if (!await JobExists(job.Metadata.Name, job.Metadata.NamespaceProperty))
                {
                    completed = true;
                }
                else
                {
                    try
                    {
                        var status = await _k8sClient.BatchV1.ReadNamespacedJobStatusAsync(
                            job.Metadata.Name, job.Metadata.NamespaceProperty);

                        if (status?.Status?.Succeeded > 0 || status?.Status?.Failed > 0)
                        {
                            completed = true;
                        }
                        else
                        {
                            _logger.LogWarning("Job still running, retrying log stream...");
                            await _backoffTracker.WaitAsync();
                        }
                    }
                    catch (Exception ex2)
                    {
                        _logger.LogWarning(ex2, "Error reading job status, retrying...");
                        await _backoffTracker.WaitAsync();
                    }
                }
            }
        }

        _logger.LogInformation("Log reading finished for job {jobName}", job.Metadata.Name);
    }

    private async Task<bool> CollectFinalLogs(V1Pod pod, StringBuilder outputBuilder)
    {
        _backoffTracker.Reset();
        bool podDeleted = false;

        while (!podDeleted)
        {
            try
            {
                using var finalLogStream = await _k8sClient.CoreV1.ReadNamespacedPodLogAsync(
                    pod.Metadata.Name,
                    pod.Metadata.NamespaceProperty,
                    follow: false);
                using var reader = new StreamReader(finalLogStream);
                outputBuilder.Append(await reader.ReadToEndAsync());
                break;
            }
            catch (Exception ex)
            {
                if (IsNotFound(ex))
                {
                    _logger.LogDebug("Pod already deleted, skipping remaining operations");
                    podDeleted = true;
                }
                else
                {
                    _logger.LogWarning(ex, "Error reading final logs, retrying...");
                    await _backoffTracker.WaitAsync();
                }
            }
        }

        return podDeleted;
    }

    private async Task<int> GetExitCode(V1Pod pod)
    {
        _backoffTracker.Reset();
        int exitCode = -1;
        while (true)
        {
            try
            {
                pod = await _k8sClient.CoreV1.ReadNamespacedPodAsync(pod.Metadata.Name, pod.Metadata.NamespaceProperty);
                var containerStatus = pod.Status?.ContainerStatuses?.FirstOrDefault();
                if (containerStatus?.State?.Terminated != null)
                {
                    exitCode = containerStatus.State.Terminated.ExitCode;
                    _logger.LogDebug("Job Exit Code: {exitCode}", exitCode);
                }
                break;
            }
            catch (Exception ex)
            {
                if (IsNotFound(ex))
                {
                    _logger.LogDebug("Pod deleted during exit code retrieval");
                    break;
                }
                _logger.LogWarning(ex, "Error retrieving exit code, retrying...");
                await _backoffTracker.WaitAsync();
            }
        }

        return exitCode;
    }

    private async Task EnsureJobDeleted(string jobName, string jobNamespace)
    {
        _backoffTracker.Reset();
        while (await JobExists(jobName, jobNamespace))
        {
            try
            {
                await _k8sClient.BatchV1.DeleteNamespacedJobAsync(
                    jobName,
                    jobNamespace,
                    new V1DeleteOptions(propagationPolicy: "Foreground"));
                _logger.LogDebug("Job delete request sent");
            }
            catch (Exception ex)
            {
                if (IsNotFound(ex))
                {
                    _logger.LogDebug("Job already deleted");
                    break;
                }
                _logger.LogWarning(ex, "Error deleting job, retrying...");
            }

            await _backoffTracker.WaitAsync();
        }
    }

    private async Task<bool> JobExists(string jobName, string jobNamespace)
    {
        var checkComplete = false;
        while (!checkComplete)
        {
            try
            {
                await _k8sClient.BatchV1.ReadNamespacedJobAsync(jobName, jobNamespace);
                checkComplete = true;
            }
            catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking Job existence for {jobName}, retrying...", jobName);
                await _backoffTracker.WaitAsync();
            }
        }

        return true;
    }

    private bool IsNotFound(Exception ex)
    {
        return ex is HttpOperationException httpEx &&
               httpEx.Response?.StatusCode == HttpStatusCode.NotFound;
    }

    private async Task<V1PodSecurityContext> GetSecurityContext()
    {
        long uid = 1000;
        long gid = 1000;

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                var procStatus = await System.IO.File.ReadAllTextAsync("/proc/self/status");
                uid = int.Parse(procStatus
                    .Split('\n')
                    .First(l => l.StartsWith("Uid:"))
                    .Split('\t')[1]);

                gid = int.Parse(procStatus
                    .Split('\n')
                    .First(l => l.StartsWith("Gid:"))
                    .Split('\t')[1]);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception getting uid/gid, using defaults.");
            }
        }

        var securityContext = new V1PodSecurityContext
        {
            RunAsUser = uid,
            RunAsGroup = gid,
            RunAsNonRoot = true
        };

        return securityContext;
    }

    private (V1Volume[] Volumes, V1VolumeMount[] VolumeMounts) GetVolumes()
    {
        var volumes = new List<V1Volume>();
        var volumeMounts = new List<V1VolumeMount>();

        V1VolumeMount volumeMount;
        V1Volume volume;

        if (_options.KubernetesJobs.UseHostVolume)
        {
            volumeMount = new V1VolumeMount
            {
                Name = _hostVolumeName,
                MountPath = _options.KubernetesJobs.VolumeMountPath,
            };

            volume = new V1Volume
            {
                Name = _hostVolumeName,
                HostPath = new V1HostPathVolumeSource
                {
                    Path = _options.KubernetesJobs.HostVolumePath,
                    Type = "DirectoryOrCreate"
                }
            };
        }
        else
        {
            volumeMount = new V1VolumeMount
            {
                Name = _appName, // can be any string as long as VolumeMount and Volume names match
                MountPath = _options.KubernetesJobs.VolumeMountPath,
                SubPath = _options.KubernetesJobs.VolumeMountSubPath,
            };

            volume = new V1Volume
            {
                Name = _appName,
                PersistentVolumeClaim = new V1PersistentVolumeClaimVolumeSource
                {
                    ClaimName = _options.KubernetesJobs.PersistentVolumeClaimName
                }
            };
        }

        volumes.Add(volume);
        volumeMounts.Add(volumeMount);

        foreach (var configMap in _options.KubernetesJobs.ConfigMaps)
        {
            var mount = new V1VolumeMount
            {
                Name = configMap.Name,
                MountPath = configMap.MountPath
            };

            var vol = new V1Volume
            {
                Name = configMap.Name,
                ConfigMap = new V1ConfigMapVolumeSource
                {
                    Name = configMap.Name
                }
            };

            volumes.Add(vol);
            volumeMounts.Add(mount);
        }

        return (volumes.ToArray(), volumeMounts.ToArray());
    }

    private new V1EnvVar[] GetEnvironmentVariables()
    {
        return base.GetEnvironmentVariables()
            .Select(x => new V1EnvVar(x.Key, x.Value))
            .ToArray();
    }

    private string GetJobName(Workspace workspace) => $"{_appName}-{workspace.Id}";
}
