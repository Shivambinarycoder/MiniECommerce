using OrderService.DTOs;

namespace OrderService.Services
{
    public class ProductServiceClient
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _readClient;

        public ProductServiceClient(HttpClient httpClient, IConfiguration configuration, IHttpClientFactory httpClientFactory)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _readClient = httpClientFactory.CreateClient("ProductReadClient"); ;
        }

        public async Task<ProductDto?> GetProduct(int productId)
        {
            var response = await _readClient.GetAsync($"api/products/{productId}");

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<ProductDto>();

            //var baseUrl = _configuration["ProductService:BaseUrl"];
            //var response = await _httpClient.GetAsync($"{baseUrl}/api/products/{productId}");
            //if (!response.IsSuccessStatusCode)
            //{
            //    return null;
            //}
            //return await response.Content.ReadFromJsonAsync<ProductDto>();
        }
        public async Task<bool> UpdateStock(int productId, int quantity, Guid operationId)
        {
            var baseUrl = _configuration["ProductService:BaseUrl"];
            var response = await _httpClient.PutAsJsonAsync($"{baseUrl}/api/products/{productId}/stock", new UpdateStockDto
            {
                Quantity = quantity,
                OperationId = operationId
            });

            return response.IsSuccessStatusCode;
        }
        public async Task<bool> RestoreStock(int productId, int quantity, Guid operationId)
        {
            var baseUrl = _configuration["ProductService:BaseUrl"];
            var response = await _httpClient.PostAsJsonAsync( $"{baseUrl}/api/products/{productId}/stock/restore", new UpdateStockDto
            {
                Quantity = quantity,
                OperationId = operationId
            });

            return response.IsSuccessStatusCode;
        }
    }
}
