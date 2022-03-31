// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Caster.Api.Features.Vlan
{

    using System.Linq;
    using AutoMapper;    

    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<System.Object, Domain.Models.Vlan>();
            CreateMap<Domain.Models.Vlan, Vlan>();
            CreateMap<CreateVlan.Command, Domain.Models.Vlan>();
            CreateMap<Domain.Models.Vlan, int>().ConstructUsing(V => V.vlan);

            CreateMap<Domain.Models.Partition, Partition>();
            CreateMap<CreatePartition.Command, Domain.Models.Partition>(); 
            CreateMap<CreatePartitionWithRange.Command, Domain.Models.Partition>(); 
            CreateMap<CreatePool.Command, Domain.Models.Pool>();
            CreateMap<System.Object, Domain.Models.Partition>();

            CreateMap<System.Object, Domain.Models.Pool>();
            CreateMap<Domain.Models.Pool, Pool>();
        }
    }
}
