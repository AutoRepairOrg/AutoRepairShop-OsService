using OsService.Domain.Enums;

namespace OsService.Application.DTOs;

public record CreateServiceOrderRequest(
    string CustomerName,
    string CustomerPhone,
    string VehiclePlate,
    string VehicleModel,
    string? Description
);

public record AddItemRequest(
    string ServiceName,
    decimal Price,
    int Quantity
);

public record UpdateStatusRequest(string Reason);

public record ServiceOrderResponse(
    Guid Id,
    string CustomerName,
    string CustomerPhone,
    string VehiclePlate,
    string VehicleModel,
    string? Description,
    string Status,
    decimal Total,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IEnumerable<ServiceOrderHistoryResponse> History,
    IEnumerable<ServiceOrderItemResponse> Items
);

public record ServiceOrderHistoryResponse(
    ServiceOrderStatus Status,
    string Note,
    DateTime OccurredAt
);

public record ServiceOrderItemResponse(
    Guid Id,
    string ServiceName,
    decimal Price,
    int Quantity
);
