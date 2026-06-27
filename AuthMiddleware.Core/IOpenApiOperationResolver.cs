namespace AuthMiddleware.Core;

/// <summary>
/// Defines a service responsible for resolving a concrete OpenAPI operation ID
/// (e.g., "GetUserById") from an incoming HTTP method (e.g., "GET") and an arbitrary 
/// request path (e.g., "/api/users/123").
/// 
/// The resolver is the critical bridge between the gateway's raw HTTP forwarding 
/// and the SDK's context building. By knowing the exact Operation ID, the context 
/// builder can intelligently process the request and extract the appropriate variables.
/// </summary>
public interface IOpenApiOperationResolver
{
    /// <summary>
    /// Asynchronously attempts to match an HTTP method and path against the loaded OpenAPI specification
    /// to determine the corresponding Operation ID.
    /// </summary>
    /// <param name="method">
    /// The HTTP method of the original request, such as "GET", "POST", "PUT", or "DELETE".
    /// This is typically extracted from the X-Forwarded-Method header.
    /// </param>
    /// <param name="path">
    /// The original request path, such as "/api/users/123". This path may contain dynamic route 
    /// parameters which the resolver must successfully evaluate and match against templated paths 
    /// in the OpenAPI spec (e.g., "/api/users/{userId}").
    /// </param>
    /// <param name="cancellationToken">
    /// A cancellation token that can be used by other objects or threads to receive notice of cancellation.
    /// </param>
    /// <returns>
    /// A task that represents the asynchronous resolution operation. 
    /// The task result contains the resolved Operation ID (e.g., "GetUserById"), 
    /// or <c>null</c> if no matching operation could be found in the specification.
    /// </returns>
    Task<string?> ResolveOperationIdAsync(string method, string path, CancellationToken cancellationToken = default);
}
