// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using AutoMapper;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Runs
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<Domain.Models.Run, Run>()
                .ForMember(m => m.Plan, opt => opt.ExplicitExpansion())
                .ForMember(m => m.Apply, opt => opt.ExplicitExpansion())
                .ForMember(m => m.PlanId, opt => opt.MapFrom((src, dest) => src.Plan == null ? (Guid?)null : src.Plan.Id))
                .ForMember(m => m.ApplyId, opt => opt.MapFrom((src, dest) => src.Apply == null ? (Guid?)null : src.Apply.Id))
                .ForMember(m => m.CreatedBy, opt => opt.MapFrom(src => src.CreatedBy.Name))
                .ForMember(m => m.ModifiedBy, opt => opt.MapFrom(src => src.ModifiedBy.Name));
            CreateMap<Create.Command, Domain.Models.Run>();
        }
    }
}
