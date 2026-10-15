using Microsoft.Extensions.Caching.Distributed;

namespace ProductService.Services
{
    public class SafeCacheService
    {
        private readonly IDistributedCache _cache;
        private readonly ILogger<SafeCacheService> _logger;

        public SafeCacheService(IDistributedCache cache, ILogger<SafeCacheService> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        public async Task<string?> GetAsync(string key)
        {
            try
            {
                return await _cache.GetStringAsync(key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis read failed for cache key {CacheKey}", key);
                return null;
            }
        }

        public async Task SetAsync(string key,string value,TimeSpan expiration)
        {
            try
            {
                await _cache.SetStringAsync(key,value,
                    new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = expiration
                    });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis write failed for cache key {CacheKey}", key);
            }
        }

        public async Task RemoveAsync(string key)
        {
            try
            {
                await _cache.RemoveAsync(key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis removal failed for cache key {CacheKey}", key);
            }
        }
    }
}
