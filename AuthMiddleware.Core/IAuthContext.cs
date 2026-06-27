namespace AuthMiddleware.Core;

/// <summary>
/// Defines the foundational contract for any authorization context model 
/// that is built by this SDK and returned to the caller.
/// By implementing this interface, you guarantee that the context always contains 
/// the resolved OpenAPI Operation ID, which is strictly required by external 
/// decision engines (like OPA) to know which endpoint is being evaluated.
/// </summary>
public interface IAuthContext
{
    /// <summary>
    /// Gets or sets the OpenAPI Operation ID (e.g., "CreateOrder", "GetUserById") 
    /// that was dynamically resolved by matching the incoming request's HTTP method 
    /// and path against the OpenAPI Specification.
    /// 
    /// The <see cref="AuthContextMiddleware{TAuthContext}"/> will automatically inject 
    /// the resolved value into this property just before serializing the response, 
    /// so the developer does not need to manually set it inside their context builder.
    /// </summary>
    string OperationId { get; set; }
}
