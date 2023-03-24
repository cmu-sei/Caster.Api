// Copyright 2023 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Corsinvest.ProxmoxVE.Api;
using Corsinvest.ProxmoxVE.Api.Extension;
using Corsinvest.ProxmoxVE.Api.Shared.Models.Cluster;
using Microsoft.Extensions.Logging;
// using Player.Vm.Api.Domain.Models;
// using Player.Vm.Api.Domain.Proxmox.Models;
// using Player.Vm.Api.Domain.Proxmox.Options;
// using Player.Vm.Api.Domain.Vsphere.Options;

namespace Caster.Api.Domain.Services;

public interface IProxmoxService
{
    Task<string[]> SaveVm(int id, int templateId);
}

public class ProxmoxService : IProxmoxService
{
    //private readonly ProxmoxOptions _options;
    private readonly ILogger<ProxmoxService> _logger;
    private readonly PveClient _pveClient;
    //private readonly IProxmoxStateService _proxmoxStateService;

    public ProxmoxService(
            //ProxmoxOptions options,
            ILogger<ProxmoxService> logger
        //IProxmoxStateService proxmoxStateService,
        )
    {
        //_options = options;
        _logger = logger;
        //_proxmoxStateService = proxmoxStateService;

        // _pveClient = new PveClient(options.Host, _options.Port);
        // _pveClient.ApiToken = options.Token;

        _pveClient = new PveClient("pve2.pve.local", 443);
        _pveClient.ApiToken = "root@pam!Console=afb71245-4ec4-4220-a9a3-1ea2fa0718af";
    }

    public async Task<string[]> SaveVm(int id, int templateId)
    {
        List<string> errors = new List<string>();

        // find template tags
        var template = await _pveClient.GetVm(templateId);
        var templateInfo = await _pveClient.Get($"/nodes/pve2/{template.Id}/status/current");

        var templateUniqueId = templateInfo.Response.data.tags; // use pveconfig instead?

        var vm = await _pveClient.GetVm(id);

        if (!vm.IsTemplate)
        {
            var templateTask = await _pveClient.Nodes["pve2"].Qemu[id].Template.Template();
            await _pveClient.WaitForTaskToFinish(templateTask);

            if (!templateTask.IsSuccessStatusCode)
            {
                errors.Add($"Failed to convert Virtual Machine to a Template:\n{templateTask.ReasonPhrase}");
                return errors.ToArray();
            }
        }

        var dict = new Dictionary<string, object>();
        dict.Add("tags", templateUniqueId);
        dict.Add("name", template.Name);
        var tagTask = await _pveClient.Create($"/nodes/pve2/{vm.Id}/config", dict);
        await _pveClient.WaitForTaskToFinish(tagTask);

        if (!tagTask.IsSuccessStatusCode)
        {
            errors.Add("Failed to update vm tags");
            return errors.ToArray();
        }

        var destroyTask = await _pveClient.Nodes["pve2"].Qemu[templateId].DestroyVm();
        await _pveClient.WaitForTaskToFinish(destroyTask);

        if (!destroyTask.IsSuccessStatusCode)
        {
            errors.Add("Failed to delete previous template");
            return errors.ToArray();
        }

        return errors.ToArray();
    }
}