using AutoMapper;
using IraqiTradeCenterCompany.Modules.Inventory.Application.Dtos;
using IraqiTradeCenterCompany.Modules.Inventory.Domain.Entities;

namespace IraqiTradeCenterCompany.Modules.Inventory.Application.Mappings;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<Item, ItemDto>()
            .ForMember(d => d.HasImage, o => o.MapFrom(s => s.MainImageStorageKey != null));
        CreateMap<StockMovement, StockMovementDto>()
            .ForMember(d => d.TypeName, o => o.MapFrom(s => s.Type.ToString()));
    }
}
