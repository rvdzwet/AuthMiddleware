# Auth Context Middleware SDK & Kong Plugin

This repository contains the ultimate, highly-decoupled architecture for combining **Kong API Gateway**, **Open Policy Agent (OPA)**, and a **.NET 10 Auth Context SDK**.

## Overview
This architecture splits the authorization workload into three specialized layers:
1. **Kong Gateway Lua Plugin**: Intercepts edge traffic, verifies mTLS for East-West bypass, and orchestrates the context fetching and OPA enforcement.
2. **.NET 10 Auth Context SDK**: A fluent, keyed-DI middleware that dynamically maps OpenAPI operations to specific `IOperationHandler` builders to parse requests and generate rich authorization contexts.
3. **Open Policy Agent (OPA)**: Evaluates the rich context and returns a boolean `allow` decision alongside partial evaluation data (ASTs).

## Features
- **East-West mTLS Bypass**: The Kong plugin natively inspects `ssl_client_s_dn` to bypass OPA for internal traffic.
- **Fluent DI Builder**: Map handlers to OpenAPI operation IDs natively using `.NET 8+ Keyed Dependency Injection`.
- **Intelligent Fallback**: The SDK automatically falls back to an empty `DefaultAuthContext` if a specific operation lacks a mapped handler, preventing edge cases.
- **X-OPA-Result Header**: The Kong plugin base64-encodes the entire OPA evaluation result and injects it downstream so APIs can perform data filtering!
- **RFC 7807 Problem Details**: Built-in, structured error formatting for 403 and 404 responses.

## Getting Started (SDK)
Register the Auth Context SDK in your `Program.cs`:
```csharp
builder.Services.AddAuthContext(options => {
    options.EndpointPath = "/_auth";
    options.OpenApiSpecPath = "openapi.yaml";
})
.MapOperation<CreateOrderHandler>("CreateOrder");

app.UseAuthContext();
```

## Getting Started (Kong Plugin)
The Lua plugin requires Kong Gateway and `lua-resty-http`. 
Deploy it by installing the `kong-opa-auth-1.0.0-1.rockspec`.

Configure the plugin in Kong:
```json
{
  "name": "kong-opa-auth",
  "config": {
    "middleware_url": "http://my-internal-api:5000/_auth",
    "opa_url": "http://opa-service:8181/v1/data/authz/allow",
    "gateway_client_cn": "api-gateway",
    "timeout": 5000
  }
}
```

## License
MIT License
