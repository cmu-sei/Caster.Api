// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoMapper;

namespace Caster.Api.Features.Designs;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<Domain.Models.Design, Design>()
            .ForMember(m => m.Modules, opt => opt.ExplicitExpansion());
        CreateMap<Create.Command, Domain.Models.Design>();
        CreateMap<Edit.Command, Domain.Models.Design>();
    }
}