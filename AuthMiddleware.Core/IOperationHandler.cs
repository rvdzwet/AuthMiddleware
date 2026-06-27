using Microsoft.AspNetCore.Http;

namespace AuthMiddleware.Core;

/// <summary>
/// Defines an operation-specific handler responsible for building the authorization context
/// for a precise OpenAPI Operation ID (e.g., "CreateOrder").
/// 
/// Developers implement this interface for each operation that requires specific parsing 
/// (like extracting parameters from the request body or querying a database). 
/// It is dynamically resolved at runtime by the middleware using native .NET Keyed Dependency Injection.
/// </summary>
public interface IOperationHandler
{
    /// <summary>
    /// Asynchronously processes the incoming HTTP request to generate an authorization context.
    /// Since this handler is explicitly registered against a specific Operation ID via <c>MapOperation</c>,
    /// it does not need the Operation ID passed into it—it already knows exactly what it's handling.
    /// </summary>
    /// <param name="context">
    /// The active <see cref="HttpContext"/> of the incoming request. 
    /// You can inspect headers, query strings, claims, or read the request body to build the context.
    /// </param>
    /// <param name="cancellationToken">
    /// A <see cref="CancellationToken"/> used to cancel the asynchronous operation if the client disconnects.
    /// </param>
    /// <returns>
    /// A task containing the populated <see cref="IAuthContext"/> model. 
    /// If you return <c>null</c>, the middleware will automatically short-circuit and return a 403 Forbidden response.
    /// </returns>
    Task<IAuthContext?> BuildContextAsync(HttpContext context, CancellationToken cancellationToken = default);
}
