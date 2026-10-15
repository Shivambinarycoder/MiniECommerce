namespace OrderService.DTOs
{
    public class UpdateStockDto
    {
        public int Quantity { get; set; }

        public Guid OperationId { get; set; }
    }
}
