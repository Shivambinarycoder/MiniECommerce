using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using OrderService.Data;
using OrderService.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("OrderDbConnection")));
builder.Services.AddHttpClient<ProductServiceClient>();
//Cors
builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

//Resilience
#region Resilience
builder.Services
    .AddHttpClient("ProductReadClient", client =>
    {
        var baseUrl =
           builder.Configuration["ProductService:BaseUrl"];

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "ProductService:BaseUrl is missing.");
        }

        client.BaseAddress = new Uri(baseUrl);
        client.Timeout = Timeout.InfiniteTimeSpan;
    })
    .AddStandardResilienceHandler(options =>
    {
        // Maximum time for the complete request
        options.TotalRequestTimeout.Timeout =
            TimeSpan.FromSeconds(10);

        // Maximum time for one attempt
        options.AttemptTimeout.Timeout =
            TimeSpan.FromSeconds(3);

        // Retry transient failures
        options.Retry.MaxRetryAttempts = 2;
        options.Retry.Delay =
            TimeSpan.FromMilliseconds(200);

        // Circuit breaker configuration
        options.CircuitBreaker.FailureRatio = 0.5;
        options.CircuitBreaker.MinimumThroughput = 5;
        options.CircuitBreaker.SamplingDuration =
            TimeSpan.FromSeconds(30);
        options.CircuitBreaker.BreakDuration =
            TimeSpan.FromSeconds(15);
    });
#endregion


//redis
var redisConnection =
    builder.Configuration.GetConnectionString("Redis");

if (string.IsNullOrWhiteSpace(redisConnection))
{
    throw new InvalidOperationException(
        "Missing ConnectionStrings:Redis configuration.");
}

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConnection;
    options.InstanceName = "MiniECommerce:";
});
//builder.Services.AddOpenApi();
// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    const string bearerScheme = "Bearer";

    options.AddSecurityDefinition(
        bearerScheme,
        new OpenApiSecurityScheme
        {
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Paste your JWT access token here."
        });

    options.AddSecurityRequirement(document =>
        new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(
                bearerScheme, document)] = []
        });
});


// JWT authentication
var jwtSettings = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSettings["Key"]!;

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings["Issuer"],

            ValidateAudience = true,
            ValidAudience = jwtSettings["Audience"],

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey)),

            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    //app.MapOpenApi();
}
app.UseCors("AngularFrontend");
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
