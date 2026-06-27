using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AuthMiddleware.Core;

/// <summary>
/// A middleware component that intercepts requests targeting a specific endpoint (e.g., "/_auth"),
/// resolves the original HTTP operation intended by the caller against an OpenAPI specification,
/// dynamically retrieves the mapped <see cref="IOperationHandler"/> using Keyed DI,
/// and returns the generated context as a JSON response for consumption by an external decision engine (like OPA).
/// </summary>
public class AuthContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AuthContextOptions _options;
    private readonly IOpenApiOperationResolver _operationResolver;
    private readonly ILogger<AuthContextMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthContextMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next delegate/middleware in the ASP.NET Core pipeline.</param>
    /// <param name="options">The configured options dictating header names and endpoint paths.</param>
    /// <param name="operationResolver">The service responsible for matching paths to OpenAPI operation IDs.</param>
    /// <param name="logger">The logger for emitting diagnostics, warnings, and errors.</param>
    public AuthContextMiddleware(
        RequestDelegate next,
        IOptions<AuthContextOptions> options,
        IOpenApiOperationResolver operationResolver,
        ILogger<AuthContextMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _operationResolver = operationResolver;
        _logger = logger;
    }

    /// <summary>
    /// Invokes the middleware logic for the current HTTP request.
    /// </summary>
    /// <param name="context">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="problemDetailsService">The ASP.NET Core service used to format RFC 7807 problem details responses.</param>
    /// <returns>A task that represents the asynchronous middleware execution.</returns>
    public async Task InvokeAsync(HttpContext context, IProblemDetailsService problemDetailsService)
    {
        // 1. Check if the current request path matches the interception endpoint (e.g., "/_auth").
        if (!context.Request.Path.Equals(_options.EndpointPath, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // 2. Extract the original intended path and method from the request headers.
        var originalPath = GetOriginalPath(context);
        var originalMethod = GetOriginalMethod(context);

        // 3. Attempt to resolve the OpenAPI Operation ID.
        var operationId = await _operationResolver.ResolveOperationIdAsync(originalMethod, originalPath, context.RequestAborted);
        
        // If no operation ID could be matched, return 404 Not Found.
        if (string.IsNullOrEmpty(operationId))
        {
            _logger.LogWarning("No OpenAPI operation found for {Method} {Path}", originalMethod, originalPath);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            
            await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new Microsoft.AspNetCore.Mvc.ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "Operation Not Found",
                    Detail = $"The forwarded request '{originalMethod} {originalPath}' did not match any OpenAPI operation."
                }
            });
            return;
        }

        // 4. Resolve the explicit handler mapped to this specific Operation ID via Keyed Dependency Injection.
        var handler = context.RequestServices.GetKeyedService<IOperationHandler>(operationId);
        
        IAuthContext? authContext;

        if (handler != null)
        {
            // 5a. A specific handler was found! Execute it to generate the contextual authorization model.
            _logger.LogInformation("Executing mapped operation handler for {OperationId}.", operationId);
            authContext = await handler.BuildContextAsync(context, context.RequestAborted);
            
            // If the handler explicitly returns null, it denies the request.
            if (authContext == null)
            {
                _logger.LogWarning("Operation handler for {OperationId} returned null, denying request.", operationId);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                
                await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context,
                    ProblemDetails = new Microsoft.AspNetCore.Mvc.ProblemDetails
                    {
                        Status = StatusCodes.Status403Forbidden,
                        Title = "Forbidden",
                        Detail = "You do not have permission to access this operation."
                    }
                });
                return;
            }
        }
        else
        {
            // 5b. No handler was mapped. Gracefully fallback to a default, empty context.
            // This ensures OPA can still evaluate the endpoint based solely on the OperationId.
            _logger.LogInformation("No explicit handler mapped for {OperationId}. Falling back to DefaultAuthContext.", operationId);
            authContext = new DefaultAuthContext();
        }

        // 6. Guarantee that the resolved operation ID is appended to the context.
        authContext.OperationId = operationId;

        // 7. Write the successful Auth Context back to the caller as JSON.
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json";
        
        // Polymorphic serialization: ensure derived types serialize all their properties.
        await JsonSerializer.SerializeAsync(context.Response.Body, authContext, authContext.GetType(), cancellationToken: context.RequestAborted);
    }

    private string GetOriginalPath(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(_options.OriginalPathHeaderName, out var pathValues) && pathValues.Count > 0)
        {
            return pathValues[0]!;
        }
        return context.Request.Path.Value ?? string.Empty;
    }

    private string GetOriginalMethod(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(_options.OriginalMethodHeaderName, out var methodValues) && methodValues.Count > 0)
        {
            return methodValues[0]!;
        }
        return context.Request.Method;
    }
}
