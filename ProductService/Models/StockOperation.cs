namespace ProductService.Models
{
    public class StockOperation
    {
        public int Id { get; set; }

        public Guid OperationId { get; set; }

        public int ProductId { get; set; }

        public int Quantity { get; set; }

        public string OperationType { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
    }
}
