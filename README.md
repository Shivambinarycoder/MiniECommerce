# E-Commerce Microservices Architecture

## Project Overview
Developed an E-Commerce application using a Microservices Architecture with ASP.NET Core Web API, SQL Server, Redis, and an API Gateway. The application separates business functionalities into independent services to improve scalability, maintainability, and reliability.

## Technologies Used
- **Backend:** ASP.NET Core Web API (.NET)
- **Database:** Microsoft SQL Server
- **Caching:** Redis
- **API Gateway:** Centralized entry point for client requests
- **Architecture:** Microservices Architecture
- **Communication:** HTTP/REST APIs
- **Tools:** Visual Studio, Swagger, Git, and GitHub

## Microservices
- **Product Service:** Manages product information, pricing, and product details.
- **Order Service:** Handles order creation and order management.
- **Inventory Service:** Manages product stock and stock availability.
- **API Gateway:** Routes client requests to the appropriate microservices.
- **Resilience:** Implemented resilience strategies such as retries, timeouts, and circuit breakers to handle transient failures and improve microservices reliability.
- **Idempotency:** Implemented idempotency to prevent duplicate operations during repeated API requests, helping avoid duplicate orders and inventory updates.

## Key Features
- Independent services for different business functionalities.
- REST API communication between microservices.
- Centralized request routing through the API Gateway.
- SQL Server for persistent data storage.
- Redis caching to improve performance and reduce repeated database queries.
- Inventory validation before order processing.
- HTTP status codes and error handling for API responses.
- Idempotency to help prevent duplicate operations during retries.
- Resilience strategies, including retries, timeouts, and circuit breakers.
- Swagger integration for API documentation and testing.

## Benefits
- Improved scalability and maintainability.
- Independent development and deployment of services.
- Better separation of business responsibilities.
- Reduced database load through caching.
- Improved fault handling and reliability.
- Easier testing and debugging of individual services.

## Learning Outcomes
- Understanding Microservices Architecture using .NET.
- Implementing REST APIs and inter-service communication.
- Integrating SQL Server and Redis.
- Understanding API Gateway routing.
- Handling inventory updates and duplicate requests.
- Applying caching, idempotency, and resilience concepts.

