// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoMapper;

namespace Caster.Api.Features.DesignModules;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        // Map private properties so we get DesignModule.ValuesJson
        // TODO: Find a way to specify for just that specific property if possible
        ShouldMapProperty = p => p.GetMethod.IsPublic || p.GetMethod.IsPrivate;

        CreateMap<Domain.Models.DesignModule, DesignModule>()
            .ForMember("ValuesJson", opt => opt.ExplicitExpansion());
        CreateMap<Domain.Models.ModuleValue, ModuleValue>();

        CreateMap<Create.Command, Domain.Models.DesignModule>()
            .ForMember(dest => dest.Values, opt => opt.MapFrom((src, dest) => dest.Values = ModuleValue.ToDomain(src.Values)));
        CreateMap<Edit.Command, Domain.Models.DesignModule>()
            .ForMember(dest => dest.Values, opt => opt.MapFrom((src, dest) => dest.AddOrUpdateValues(ModuleValue.ToDomain(src.Values))));
    }
}