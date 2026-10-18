namespace Shop.Core.Entities;

public enum OrderStatus
{
    Pending = 0,
    Confirmed = 1,
    Processing = 2,
    Shipped = 3,
    Delivered = 4,
    Cancelled = 5,
}

public class Order
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public OrderStatus Status { get; set; }
    public string CustomerName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? Email { get; set; }
    public string Address { get; set; } = "";
    public string City { get; set; } = "";
    public string? DeliveryZone { get; set; }
    public string? Notes { get; set; }
    public string PaymentMethod { get; set; } = "CashOnDelivery";
    public decimal Subtotal { get; set; }
    public decimal ShippingFee { get; set; }
    public decimal Total { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<OrderItem> Items { get; set; } = new();
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
    public Availability Availability { get; set; }
    public int? SupplierOfferId { get; set; }
    public int? SupplierId { get; set; }
    /// <summary>Supplier cost converted to base currency at the time of the order.</summary>
    public decimal? UnitCost { get; set; }
    public DateOnly? EstimatedDeliveryDate { get; set; }
}

public class AdminUser
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public enum ImportKind
{
    Catalog = 0,
    Offers = 1,
}

public class ImportLog
{
    public int Id { get; set; }
    public ImportKind Kind { get; set; }
    public string FileName { get; set; } = "";
    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public DateTime StartedAt { get; set; }
    public int TotalRows { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Failed { get; set; }
    public string? Messages { get; set; }
}
