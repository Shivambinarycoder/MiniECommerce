namespace OrderService.DTOs
{
    public class CreateOrderDto
    {
        public int UserId { get; set; }

        public List<CreateOrderItemDto> Items { get; set; }
    }

    public class CreateOrderItemDto
    {
        public int ProductId { get; set; }

        public int Quantity { get; set; }
    }
}
