using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using OrderService.Data;
using OrderService.DTOs;
using OrderService.Models;
using OrderService.Services;
using System.Text.Json;

namespace OrderService.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly OrderDbContext _context;
        private readonly ProductServiceClient _productServiceClient;
        private readonly ILogger<OrdersController> _logger;
        private readonly IDistributedCache _cache;
        public OrdersController(OrderDbContext context, ProductServiceClient productServiceClient, ILogger<OrdersController> logger, IDistributedCache cache)
        {
            _context = context;
            _productServiceClient = productServiceClient;
            _logger = logger;
            _cache = cache;
        }

        [HttpGet]
        public async Task<IActionResult> GetOrders()
        {
            //var orders = await _context.Orders.Include(x => x.OrderItems).ToListAsync();

            //return Ok(orders);
            const string cacheKey = "orders:all";

            // 1. Check Redis
            var cachedOrders = await _cache.GetStringAsync(cacheKey);

            if (cachedOrders != null)
            {
                var ordersFromCache =
                    JsonSerializer.Deserialize<List<OrderResponseDto>>(cachedOrders);

                if (ordersFromCache != null)
                {
                    return Ok(ordersFromCache);
                }
            }


            var orders = await _context.Orders.AsNoTracking().Include(x => x.OrderItems).ToListAsync();

            var response = orders.Select(order => new OrderResponseDto
            {
                Id = order.Id,
                UserId = order.UserId,
                OrderDate = order.OrderDate,
                TotalAmount = order.TotalAmount,
                Status = order.Status,
                Items = order.OrderItems.Select(item => new OrderItemResponseDto
                {
                    Id = item.Id,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    Price = item.Price
                }).ToList()
            }).ToList();

            // 4. Cache the DTO list
            await _cache.SetStringAsync(
                cacheKey,
                JsonSerializer.Serialize(response),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                });
            //await _cache.RemoveAsync("orders:all"); add this in update method.
            return Ok(response);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetOrder(int id)
        {
            string cacheKey = $"orders:{id}";
            // 1. Check Redis
            var cachedOrder = await _cache.GetStringAsync(cacheKey);

            if (cachedOrder != null)
            {
                var orderFromCache = JsonSerializer.Deserialize<OrderResponseDto>(cachedOrder);

                if (orderFromCache != null)
                {
                    return Ok(orderFromCache);
                }
            }

            var order = await _context.Orders.AsNoTracking().Include(x => x.OrderItems).FirstOrDefaultAsync(x => x.Id == id);

            if (order == null)
            {
                return NotFound();
            }
            var response = new OrderResponseDto
            {
                Id = order.Id,
                UserId = order.UserId,
                OrderDate = order.OrderDate,
                TotalAmount = order.TotalAmount,
                Status = order.Status,
                Items = order.OrderItems.Select(item => new OrderItemResponseDto
                {
                    Id = item.Id,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    Price = item.Price
                }).ToList()
            };
            var serializedOrder = JsonSerializer.Serialize(response);

            await _cache.SetStringAsync(
                cacheKey,
                serializedOrder,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                });
            //await _cache.RemoveAsync($"orders:{id}"); add inside update method.
            return Ok(response);
        }

        [HttpGet("product/{productId}")]
        public async Task<IActionResult> GetProduct(int productId)
        {
            var product = await _productServiceClient.GetProduct(productId);

            if (product == null)
            {
                return NotFound();
            }

            return Ok(product);
        }

        [HttpPost]
        public async Task<IActionResult> CreateOrder(CreateOrderDto request)
        {
            if (request.Items == null || request.Items.Count == 0)
            {
                return BadRequest("Order must contain at least one item.");
            }

            decimal totalAmount = 0;

            var order = new Order
            {
                UserId = request.UserId,
                OrderDate = DateTime.UtcNow,
                Status = "Pending",
                TotalAmount = 0,
                OrderItems = new List<OrderItem>()
            };

            foreach (var item in request.Items)
            {
                ProductDto? product;
                try
                {
                     product = await _productServiceClient.GetProduct(item.ProductId);
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(
                        ex,
                        "ProductService unavailable while retrieving ProductId {ProductId}",
                        item.ProductId);

                    return StatusCode(
                        StatusCodes.Status503ServiceUnavailable,
                        "ProductService is temporarily unavailable. Please try again.");
                }
                catch (OperationCanceledException ex)
                {
                    _logger.LogError(
                        ex,
                        "Product lookup timed out or was cancelled for ProductId {ProductId}",
                        item.ProductId);

                    return StatusCode(
                        StatusCodes.Status503ServiceUnavailable,
                        "ProductService did not respond in time. Please try again.");
                }
                if (product == null)
                {
                    return BadRequest(
                        $"Product {item.ProductId} does not exist.");
                }

                if (item.Quantity <= 0)
                {
                    return BadRequest(
                        $"Quantity for product {item.ProductId} must be greater than zero.");
                }

                if (product.StockQuantity < item.Quantity)
                {
                    return BadRequest(
                        $"Not enough stock for product {item.ProductId}.");
                }

                var itemTotal = product.Price * item.Quantity;

                totalAmount += itemTotal;

                order.OrderItems.Add(new OrderItem
                {
                    ProductId = product.Id,
                    Quantity = item.Quantity,
                    Price = product.Price
                });
            }

            order.TotalAmount = totalAmount;
            var deductedStocks = new List<(int ProductId, int Quantity, Guid RestoreOperationId)>();
            // Step 1: Reduce stock through ProductService
            foreach (var item in request.Items)
            {
                var operationId = Guid.NewGuid();
                bool stockUpdated;

                try
                {
                   
                    stockUpdated = await _productServiceClient
                        .UpdateStock(item.ProductId, item.Quantity, operationId);
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(ex, "ProductService request failed for ProductId {ProductId}", item.ProductId);
                    stockUpdated = false;
                }

                if (!stockUpdated)
                {
                    _logger.LogWarning("Stock deduction failed for ProductId {ProductId}. Starting restoration.", item.ProductId);

                    // Restore any stock already deducted for this order.
                    foreach (var deducted in deductedStocks)
                    {
                        try
                        {
                           
                            bool restored =await _productServiceClient.RestoreStock(
                                deducted.ProductId,
                                deducted.Quantity,
                                operationId);

                            if (!restored)
                            {
                                _logger.LogCritical(
                                    "Stock restoration failed for ProductId {ProductId}, " +
                                    "Quantity {Quantity}. Manual recovery may be required.",
                                    deducted.ProductId,
                                    deducted.Quantity);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogCritical(
                                ex,
                                "Stock restoration threw an exception for ProductId {ProductId}, " +
                                "Quantity {Quantity}. Manual recovery may be required.",
                                deducted.ProductId,
                                deducted.Quantity);
                        }
                    }

                    return Conflict(
                        $"Could not update stock for product {item.ProductId}.");
                }

                // Record only successful deductions.
                deductedStocks.Add((item.ProductId, item.Quantity, Guid.NewGuid()));
            }
            _context.Orders.Add(order);

            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Order {OrderId} created successfully for UserId {UserId}. Total: {TotalAmount}", order.Id, order.UserId, order.TotalAmount);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Failed to save order for UserId {UserId}. Attempting stock restoration.", request.UserId);
                foreach (var deducted in deductedStocks)
                {
                    try
                    {
                        await _productServiceClient.RestoreStock(
                            deducted.ProductId,
                            deducted.Quantity,
                            deducted.RestoreOperationId);
                    }
                    catch (HttpRequestException)
                    {
                        // Restoration failed; this needs operational recovery.
                    }
                }

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    "Order could not be saved. Stock restoration was attempted.");
            }
            var response = new OrderResponseDto
            {
                Id = order.Id,
                UserId = order.UserId,
                OrderDate = order.OrderDate,
                TotalAmount = order.TotalAmount,
                Status = order.Status,
                Items = order.OrderItems.Select(item => new OrderItemResponseDto
                {
                    Id = item.Id,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    Price = item.Price
                }).ToList()
            };

            return Ok(response);
        }
    }
}
