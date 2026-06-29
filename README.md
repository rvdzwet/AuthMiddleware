# Auth Context Middleware SDK & Kong Plugin

This repository contains the ultimate, highly-decoupled architecture for combining **Kong API Gateway**, **Open Policy Agent (OPA)**, and a **.NET 10 Auth Context SDK**.

## Overview
This architecture splits the authorization workload into three specialized layers:
1. **Envoy WebAssembly (Wasm) Filter**: Intercepts Kong Mesh sidecar traffic, handles mTLS East-West bypass, and orchestrates the context fetching and OPA enforcement natively in Envoy.
2. **.NET 10 Auth Context SDK**: A fluent, keyed-DI middleware that dynamically maps OpenAPI operations to specific `IOperationHandler` builders to parse requests and generate rich authorization contexts.
3. **Open Policy Agent (OPA)**: Evaluates the rich context and returns a boolean `allow` decision alongside partial evaluation data (ASTs).

## Features
- **East-West mTLS Bypass**: The Wasm filter natively inspects `x-forwarded-client-cert` to bypass OPA for internal traffic.
- **Fluent DI Builder**: Map handlers to OpenAPI operation IDs natively using `.NET 8+ Keyed Dependency Injection`.
- **Intelligent Fallback**: The SDK automatically falls back to an empty `DefaultAuthContext` if a specific operation lacks a mapped handler, preventing edge cases.
- **X-OPA-Result Header**: The Wasm filter base64-encodes the entire OPA evaluation result and injects it downstream so APIs can perform data filtering!
- **RFC 7807 Problem Details**: Built-in, structured error formatting for 403 and 404 responses.

---

## Developer Guide: .NET Auth Context SDK

The .NET SDK is designed to be highly extensible. Rather than writing a monolithic authorization method, you create isolated **Context Models** and **Operation Handlers** tailored specifically to individual OpenAPI operations. 

Here is the verbose step-by-step guide for implementing it in your API.

### 1. Define a Context Model
Create a C# class that represents the specific data OPA needs to authorize a particular endpoint. Your class **must** implement `IAuthContext` (which guarantees the `OperationId` is included).

```csharp
using AuthMiddleware.Core;

// This context contains data specific to creating an order
public class CreateOrderContext : IAuthContext
{
    public string OperationId { get; set; } = string.Empty;
    
    // Custom properties required by OPA
    public string UserId { get; set; }
    public decimal OrderAmount { get; set; }
    public string TenantId { get; set; }
}
```

### 2. Define an Operation Handler
Create a class implementing `IOperationHandler`. This handler is responsible for inspecting the incoming `HttpContext`, reading headers or JSON bodies, querying databases, and constructing your `CreateOrderContext`.

```csharp
using AuthMiddleware.Core;
using Microsoft.AspNetCore.Http;
using System.Text.Json;

public class CreateOrderHandler : IOperationHandler
{
    public async Task<IAuthContext?> BuildContextAsync(HttpContext context, CancellationToken ct)
    {
        // 1. Extract identity (e.g. from JWT or downstream headers)
        var userId = context.Request.Headers["X-User-Id"].FirstOrDefault() ?? "anonymous";
        var tenantId = context.Request.Headers["X-Tenant-Id"].FirstOrDefault();

        // 2. Read the body to get the OrderAmount (Be careful, this requires buffering in real apps)
        // context.Request.EnableBuffering();
        // var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: ct);
        // var amount = body.RootElement.GetProperty("amount").GetDecimal();

        var authContext = new CreateOrderContext
        {
            UserId = userId,
            TenantId = tenantId,
            OrderAmount = 500.00m // Hardcoded for this example
        };

        // Return the context. 
        // If you return `null`, the SDK immediately rejects the request with a 403 Forbidden.
        return authContext;
    }
}
```

### 3. Register with Fluent Dependency Injection
Wire up your handler in your `Program.cs`. The SDK uses **.NET Keyed Dependency Injection** to map the `CreateOrder` OpenAPI Operation ID exactly to your `CreateOrderHandler`.

```csharp
// 1. Configure Problem Details so 403/404s are formatted correctly (RFC 7807)
builder.Services.AddProblemDetails();

// 2. Register the Auth Context Middleware
builder.Services.AddAuthContext(options => {
    // The endpoint path where the Kong plugin sends sub-requests
    options.EndpointPath = "/_auth";
    
    // The path to your OpenAPI Specification
    options.OpenApiSpecPath = Path.Combine(builder.Environment.ContentRootPath, "openapi.yaml");
    
    // (Optional) Header configuration for proxying
    options.OriginalPathHeaderName = "X-Forwarded-Path";
    options.OriginalMethodHeaderName = "X-Forwarded-Method";
})
// 3. Map Operations to Handlers fluently!
.MapOperation<CreateOrderHandler>("CreateOrder")
.MapOperation<GetUserHandler>("GetUserById"); 

var app = builder.Build();

// 4. Insert into the HTTP Pipeline
app.UseAuthContext();
```

### What if an Operation isn't Mapped? (The Fallback)
If Kong forwards a request to an endpoint (e.g., `GetServerStatus`) that you haven't explicitly mapped via `.MapOperation()`, the SDK will **not** crash. 
Instead, it gracefully falls back to a built-in `DefaultAuthContext`. It will return a 200 OK JSON payload containing just `{"OperationId": "GetServerStatus"}`. This allows your OPA engine to authorize simple endpoints based purely on the Operation ID!

### Downstream OPA Enforcement (`X-OPA-Result`)
After the Kong Plugin consults your middleware and then asks OPA for permission, it forwards the traffic to your actual API endpoints. 

If OPA performs **Partial Evaluation** (returning data filters or ASTs instead of just `allow: true`), the Kong plugin Base64-encodes that exact JSON result and injects it into the `X-OPA-Result` header. 

You can read this header in your downstream controllers:
```csharp
[HttpGet]
public IActionResult GetOrders()
{
    var b64Result = Request.Headers["X-OPA-Result"].FirstOrDefault();
    if (b64Result != null) 
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(b64Result));
        // Deserialize JSON to apply OPA data filtering to Entity Framework!
    }
    return Ok();
}
```

---

## Getting Started (Envoy Wasm Filter)
The WebAssembly filter requires Kong Mesh (Kuma) or any standard Envoy proxy. 

1. Compile the Go module using TinyGo:
```bash
cd WasmFilter
tinygo build -o kong-opa-auth.wasm -scheduler=none -target=wasi main.go
```

2. Configure the plugin in Kong Mesh using a `MeshWasmFilter` policy:
```yaml
apiVersion: kuma.io/v1alpha1
kind: MeshWasmFilter
metadata:
  name: auth-context-opa-filter
  mesh: default
spec:
  targetRef:
    kind: Mesh
  config:
    wasm:
      url: "https://your-artifact-repo/kong-opa-auth.wasm"
```

## License
MIT License
