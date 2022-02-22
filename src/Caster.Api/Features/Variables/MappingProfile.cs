// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoMapper;

namespace Caster.Api.Features.Variables;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<Domain.Models.Variable, Variable>();
        CreateMap<Create.Command, Domain.Models.Variable>();
        CreateMap<Edit.Command, Domain.Models.Variable>();
    }
}