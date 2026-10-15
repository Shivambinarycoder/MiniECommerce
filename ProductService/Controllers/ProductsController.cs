using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using ProductService.Data;
using ProductService.DTOs;
using ProductService.Models;
using ProductService.Services;
using System.Text.Json;

namespace ProductService.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ProductsController : ControllerBase
    {
        private readonly ProductDbContext _context;
        private readonly SafeCacheService _safeCache;
        public ProductsController(ProductDbContext context, SafeCacheService safeCache)
        {
            _context = context;
            _safeCache = safeCache;
        }

        [HttpGet]
        public async Task<IActionResult> GetProducts()
        {
            const string cacheKey = "products:all";
            // 1. Check Redis
            var cachedProducts = await _safeCache.GetAsync(cacheKey);

            if (cachedProducts != null)
            {
                var productsFromCache = JsonSerializer.Deserialize<List<Product>>(cachedProducts);

                if (productsFromCache != null)
                {
                    return Ok(productsFromCache);
                }
            }
            // 2. Cache miss: fetch products from SQL Server
            var products = await _context.Products.AsNoTracking().ToListAsync();

            // 3. Store the result in Redis for 5 minutes
            await _safeCache.SetAsync(cacheKey,JsonSerializer.Serialize(products),TimeSpan.FromMinutes(5));
           // var serializedProducts = JsonSerializer.Serialize(products);

            //await _cache.SetStringAsync(cacheKey,serializedProducts,new DistributedCacheEntryOptions
            //    {
            //        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
            //    });

            return Ok(products);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetProduct(int id)
        {
            string cacheKey = $"product:{id}";

            // 1. Check Redis first
            var cachedProduct = await _safeCache.GetAsync(cacheKey);

            if (cachedProduct != null)
            {
                var productFromCache =
                    JsonSerializer.Deserialize<Product>(cachedProduct);

                if (productFromCache != null)
                    return Ok(productFromCache);
            }

            // 2. Cache miss: get product from SQL Server
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                return NotFound();
            }

            // 3. Store product in Redis for 5 minutes
            await _safeCache.SetAsync(cacheKey, JsonSerializer.Serialize(product), TimeSpan.FromMinutes(5));
            return Ok(product);
        }

        [Authorize(Roles = "Admin")]
        [HttpPut("{id}/stock")]
        public async Task<IActionResult> UpdateStock(int id, UpdateStockDto request)
        {
            if (request.OperationId == Guid.Empty)
                return BadRequest("OperationId is required.");

            if (request.Quantity <= 0)
            {
                return BadRequest("Quantity must be greater than zero.");
            }

            await using var transaction =
                          await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

            // 1. Check whether this operation was already processed
            var existingOperation = await _context.StockOperations
                .FirstOrDefaultAsync(x =>x.OperationId == request.OperationId);

            if (existingOperation != null)
            {
                // Prevent reusing the same ID for a different request
                if (existingOperation.ProductId != id ||
                    existingOperation.Quantity != request.Quantity ||
                    existingOperation.OperationType != "Deduct")
                {
                    return Conflict("OperationId was already used for a different operation.");
                }

                return Ok("Stock deduction was already processed.");
            }

            var rowsUpdated = await _context.Products
                .Where(p =>
                    p.Id == id &&
                    p.StockQuantity >= request.Quantity)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(
                        p => p.StockQuantity,
                        p => p.StockQuantity - request.Quantity));

            if (rowsUpdated == 0)
            {
                var exists = await _context.Products
                    .AnyAsync(p => p.Id == id);

                if (!exists)
                {
                    return NotFound(
                        $"Product with ID {id} was not found.");
                }

                return Conflict("Insufficient stock.");
            }
            // 3. Record the successful operation
            _context.StockOperations.Add(new StockOperation
            {
                OperationId = request.OperationId,
                ProductId = id,
                Quantity = request.Quantity,
                OperationType = "Deduct",
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            // 4. Commit stock change and operation record together
            await transaction.CommitAsync();

            // Remove potentially stale product data
            await _safeCache.RemoveAsync($"product:{id}");
            await _safeCache.RemoveAsync("products:all");

            var product = await _context.Products
                .Where(p => p.Id == id)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.StockQuantity
                }).FirstAsync();

            return Ok(product);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("{id}/stock/restore")]
        public async Task<IActionResult> RestoreStock(int id, UpdateStockDto request)
        {
            if (request.OperationId == Guid.Empty)
                return BadRequest("OperationId is required.");

            if (request.Quantity <= 0)
            {
                return BadRequest("Quantity must be greater than zero.");
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(
           System.Data.IsolationLevel.Serializable);

            // 1. Check whether this restoration was already processed
            var existingOperation = await _context.StockOperations
                .FirstOrDefaultAsync(x =>
                    x.OperationId == request.OperationId);

            if (existingOperation != null)
            {
                if (existingOperation.ProductId != id ||
                    existingOperation.Quantity != request.Quantity ||
                    existingOperation.OperationType != "Restore")
                {
                    return Conflict(
                        "OperationId was already used for a different operation.");
                }

                return Ok("Stock restoration was already processed.");
            }

            var rowsUpdated = await _context.Products
                .Where(p => p.Id == id)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(
                        p => p.StockQuantity,
                        p => p.StockQuantity + request.Quantity));

            if (rowsUpdated == 0)
            {
                return NotFound($"Product with ID {id} was not found.");
            }

            // 3. Record the successful restoration
            _context.StockOperations.Add(new StockOperation
            {
                OperationId = request.OperationId,
                ProductId = id,
                Quantity = request.Quantity,
                OperationType = "Restore",
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            // 4. Invalidate cache after the commit
            await _safeCache.RemoveAsync($"product:{id}");
            await _safeCache.RemoveAsync("products:all");

            var product = await _context.Products
                .Where(p => p.Id == id)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.StockQuantity
                })
                .FirstAsync();

            return Ok(product);
        }
    }
}
