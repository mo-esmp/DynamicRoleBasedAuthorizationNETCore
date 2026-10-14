namespace SampleWebApp;

public sealed record SampleOrder(int Id, string Customer, decimal Total, string Status);

public static class SampleOrders
{
    public static IReadOnlyList<SampleOrder> All { get; } =
    [
        new(1001, "Contoso", 1250.00m, "Shipped"),
        new(1002, "Fabrikam", 310.50m, "Processing"),
        new(1003, "Northwind Traders", 89.99m, "Pending"),
        new(1004, "Adventure Works", 4020.00m, "Shipped"),
    ];
}
