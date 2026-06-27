using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;

namespace AuthMiddleware.Core;

/// <summary>
/// The default implementation of <see cref="IOpenApiOperationResolver"/>.
/// This class dynamically fetches the OpenAPI specification using the registered <see cref="IOpenApiSpecProvider"/>,
/// parses the document, compiles regular expressions for every defined route, and matches incoming 
/// requests to their corresponding OpenAPI operation IDs.
/// 
/// This class is registered as a Singleton and is completely thread-safe. 
/// It utilizes lazy initialization to ensure the heavy parsing and Regex compilation 
/// happens exactly once upon the first incoming request, even under massive concurrent load.
/// </summary>
public class OpenApiOperationResolver : IOpenApiOperationResolver
{
    private readonly IOpenApiSpecProvider _specProvider;
    
    // The cached list of route entries. Once populated, this is entirely immutable and thread-safe for reading.
    private List<RouteEntry>? _routes;
    
    // A semaphore used to securely lock the initialization block, guaranteeing the spec is only parsed once.
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenApiOperationResolver"/> class.
    /// </summary>
    /// <param name="specProvider">The provider used to fetch the OpenAPI document stream.</param>
    public OpenApiOperationResolver(IOpenApiSpecProvider specProvider)
    {
        _specProvider = specProvider;
    }

    /// <summary>
    /// Ensures the OpenAPI specification is downloaded, parsed, and cached.
    /// If the specification is already loaded, this method returns instantly.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for aborting the initialization.</param>
    /// <returns>An asynchronous task representing the initialization.</returns>
    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        // Fast path: if routes are already cached, return immediately without locking.
        if (_routes != null) return;

        // Slow path: acquire the lock to initialize the routes.
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            // Double-check locking pattern: ensure another thread didn't initialize it while we were waiting.
            if (_routes != null) return;

            var routes = new List<RouteEntry>();
            
            // 1. Fetch the specification stream from the abstract provider.
            using var stream = await _specProvider.GetOpenApiSpecAsync(cancellationToken);
            
            // 2. Parse the stream into an OpenApiDocument object model.
            var document = new OpenApiStreamReader().Read(stream, out var diagnostic);

            // 3. Iterate over every path defined in the OpenAPI specification.
            if (document?.Paths != null)
            {
                foreach (var (path, pathItem) in document.Paths)
                {
                    // Convert OpenAPI path templating (e.g., "/users/{id}") into a Regex pattern (e.g., "^/users/([^/]+)$")
                    // This allows us to rapidly match incoming raw URLs against the templated paths.
                    var regexPattern = "^" + Regex.Replace(path, @"\{[^}]+\}", "([^/]+)") + "$";
                    var regex = new Regex(regexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

                    // Iterate over every HTTP method defined for this specific path.
                    foreach (var (operationType, operation) in pathItem.Operations)
                    {
                        // We only cache operations that actually possess an OperationId.
                        if (!string.IsNullOrWhiteSpace(operation.OperationId))
                        {
                            routes.Add(new RouteEntry
                            {
                                Method = operationType.ToString().ToUpperInvariant(),
                                PathRegex = regex,
                                OperationId = operation.OperationId
                            });
                        }
                    }
                }
            }
            
            // 4. Atomically publish the fully constructed routes list to the field, satisfying the double-check lock.
            _routes = routes;
        }
        finally
        {
            // Always release the lock, even if parsing throws an exception.
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Evaluates an incoming HTTP method and URL path against the cached OpenAPI routing table 
    /// to resolve the Operation ID.
    /// </summary>
    /// <param name="method">The HTTP method (e.g., "GET").</param>
    /// <param name="path">The HTTP URL path (e.g., "/api/users/123").</param>
    /// <param name="cancellationToken">A token for cancelling the operation.</param>
    /// <returns>The resolved OpenAPI Operation ID, or null if no match was found.</returns>
    public async Task<string?> ResolveOperationIdAsync(string method, string path, CancellationToken cancellationToken = default)
    {
        // Guarantee the routing table is loaded before attempting to match.
        await EnsureLoadedAsync(cancellationToken);

        // Normalize the HTTP method for safe comparison.
        var normalizedMethod = method.ToUpperInvariant();
        
        // Linearly scan the cached routing table to find a matching method and regex path.
        // For extremely large APIs, this could be optimized further into a Radix tree, 
        // but linear Regex matching is extremely fast for standard enterprise APIs.
        foreach (var route in _routes!)
        {
            if (route.Method == normalizedMethod && route.PathRegex.IsMatch(path))
            {
                return route.OperationId;
            }
        }

        // Return null to signify that this request does not belong to the API schema.
        return null;
    }

    /// <summary>
    /// Represents an internal, immutable, cached routing entry mapping a Regex path 
    /// and HTTP method to a specific OpenAPI Operation ID.
    /// </summary>
    private class RouteEntry
    {
        /// <summary>
        /// The uppercase HTTP method required for this route (e.g., "POST").
        /// </summary>
        public required string Method { get; init; }
        
        /// <summary>
        /// The compiled Regular Expression used to evaluate raw request paths against this route.
        /// </summary>
        public required Regex PathRegex { get; init; }
        
        /// <summary>
        /// The OpenAPI Operation ID mapped to this route.
        /// </summary>
        public required string OperationId { get; init; }
    }
}
