// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Caster.Api.Features.Hosts
{
    using AutoMapper;

    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<Domain.Models.Host, Host>();
            CreateMap<Create.Command, Domain.Models.Host>();
            CreateMap<Edit.Command, Domain.Models.Host>();
            CreateMap<PartialEdit.Command, Domain.Models.Host>()
                .ForMember(dest => dest.ProjectId, opt => opt.MapFrom((src, dest) => src.ProjectId.HasValue ? src.ProjectId.Value : dest.ProjectId))
                .ForAllMembers(opts => opts.Condition((src, dest, srcMember) => srcMember != null));
        }
    }
}
