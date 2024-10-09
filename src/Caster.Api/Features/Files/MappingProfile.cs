// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using AutoMapper;

namespace Caster.Api.Features.Files
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<Domain.Models.File, File>()
                .ForMember(m => m.Content, opt => opt.ExplicitExpansion())
                .ForMember(m => m.ModifiedByName, opt => opt.MapFrom((src, dest) => src.ModifiedBy.Name))
                .ForMember(m => m.LockedByName, opt => opt.MapFrom((src) => src.LockedBy.Name));
            CreateMap<Create.Command, Domain.Models.File>();
            CreateMap<Edit.Command, Domain.Models.File>();
            CreateMap<PartialEdit.Command, Domain.Models.File>()
                .ForMember(dest => dest.WorkspaceId, opt => opt.MapFrom((src, dest) => src.WorkspaceId.HasValue ? src.WorkspaceId.Value : dest.WorkspaceId))
                .ForAllMembers(opts => opts.Condition((src, dest, srcMember) => srcMember != null));
            CreateMap<Domain.Models.FileVersion, FileVersion>()
                .ForMember(m => m.Content, opt => opt.ExplicitExpansion())
                .ForMember(m => m.ModifiedByName, opt => opt.MapFrom((src, dest) => src.ModifiedBy.Name))
                .ForMember(m => m.TaggedByName, opt => opt.MapFrom((src) => src.TaggedBy.Name));
            CreateMap<Domain.Models.File, Domain.Models.FileVersion>()
                .ForMember(dest => dest.FileId, opt => opt.MapFrom(src => src.Id))
                .ForMember(dest => dest.Id, opt => opt.Ignore());
        }
    }
}
