// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Caster.Api.Features.Vlan
{
    using AutoMapper;

    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<Domain.Models.Vlan, Vlan>();
            CreateMap<PartialEditVlan.Command, Domain.Models.Vlan>()
                .ForAllMembers(opts => opts.Condition((src, dest, srcMember) => srcMember != null));

            CreateMap<Domain.Models.Partition, Partition>();
            CreateMap<CreatePartition.Command, Domain.Models.Partition>()
                .ForMember(x => x.Vlans, opt => opt.Ignore());
            CreateMap<EditPartition.Command, Domain.Models.Partition>();
            CreateMap<PartialEditPartition.Command, Domain.Models.Partition>()
                .ForAllMembers(opts => opts.Condition((src, dest, srcMember) => srcMember != null));

            CreateMap<CreatePool.Command, Domain.Models.Pool>();
            CreateMap<EditPool.Command, Domain.Models.Pool>();
            CreateMap<PartialEditPool.Command, Domain.Models.Pool>()
                .ForAllMembers(opts => opts.Condition((src, dest, srcMember) => srcMember != null));

            CreateMap<Domain.Models.Pool, Pool>();
        }
    }
}
