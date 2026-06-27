namespace AuthMiddleware.Core;

/// <summary>
/// Provides configuration options for defining the behavior of the Auth Context Middleware.
/// These options dictate how the middleware routes requests and which headers it expects 
/// from the upstream API Gateway.
/// </summary>
public class AuthContextOptions
{
    /// <summary>
    /// Gets or sets the relative endpoint path where the middleware will intercept incoming requests.
    /// When the gateway forwards a request to this path, the middleware will execute its logic.
    /// The default value is "/_auth".
    /// </summary>
    public string EndpointPath { get; set; } = "/_auth";

    /// <summary>
    /// Gets or sets the absolute or relative path to the OpenAPI Specification (OAS) file.
    /// This can point to either a JSON or YAML file containing the API schema.
    /// Used by the default FileOpenApiSpecProvider to load the specification stream.
    /// </summary>
    public string OpenApiSpecPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the HTTP header that contains the original requested path.
    /// API gateways typically rewrite the URL when forwarding to the middleware, so this header 
    /// is crucial for resolving the OpenAPI operation.
    /// The default value is "X-Forwarded-Path".
    /// </summary>
    public string OriginalPathHeaderName { get; set; } = "X-Forwarded-Path";

    /// <summary>
    /// Gets or sets the name of the HTTP header that contains the original requested HTTP method.
    /// API gateways may change the HTTP method when forwarding, so this header ensures the middleware
    /// evaluates the true intended action (e.g., POST, DELETE).
    /// The default value is "X-Forwarded-Method".
    /// </summary>
    public string OriginalMethodHeaderName { get; set; } = "X-Forwarded-Method";
}
