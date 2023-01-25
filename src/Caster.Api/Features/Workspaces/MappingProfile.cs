// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoMapper;

namespace Caster.Api.Features.Workspaces
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<Domain.Models.Workspace, Workspace>();
            CreateMap<Create.Command, Domain.Models.Workspace>();
            CreateMap<Edit.Command, Domain.Models.Workspace>();
            CreateMap<PartialEdit.Command, Domain.Models.Workspace>()
                .ForMember(dest => dest.Parallelism, opt => opt.MapFrom((src, dest) => src.Parallelism.HasValue ? src.Parallelism.Value : dest.Parallelism))
                .ForMember(dest => dest.AzureDestroyFailureThreshold,
                    opt => opt.MapFrom((src, dest) => src.AzureDestroyFailureThreshold.HasValue ? src.AzureDestroyFailureThreshold.Value : dest.AzureDestroyFailureThreshold))
                .ForAllOtherMembers(opts => opts.Condition((src, dest, srcMember) => srcMember != null));
        }
    }
}
