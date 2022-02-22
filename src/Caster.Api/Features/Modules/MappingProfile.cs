// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using AutoMapper;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Modules
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<Domain.Models.Module, Module>()
                .ForMember(m => m.Versions, opt => opt.ExplicitExpansion());
            CreateMap<Domain.Models.ModuleVersion, ModuleVersion>();
            CreateMap<Domain.Models.ModuleVariable, ModuleVariable>();
            CreateMap<Domain.Models.ModuleOutput, ModuleOutput>();
        }
    }
}
