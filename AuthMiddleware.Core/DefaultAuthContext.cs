namespace AuthMiddleware.Core;

/// <summary>
/// Represents a basic, empty authorization context that serves as the ultimate fallback.
/// If a request matches an OpenAPI operation (e.g., "GetServerStatus") but the developer 
/// hasn't registered a specific <see cref="IOperationHandler"/> for it, the middleware 
/// will automatically instantiate this default context.
/// 
/// This elegantly ensures that OPA always receives a 200 OK JSON payload containing 
/// at minimum the <see cref="IAuthContext.OperationId"/>, allowing OPA to make an authorization 
/// decision based purely on the endpoint being accessed, without failing the request.
/// </summary>
public class DefaultAuthContext : IAuthContext
{
    /// <summary>
    /// Gets or sets the OpenAPI Operation ID. The middleware guarantees this is populated.
    /// </summary>
    public string OperationId { get; set; } = string.Empty;
}
