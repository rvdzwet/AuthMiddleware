namespace AuthMiddleware.Core;

/// <summary>
/// Defines a provider that is responsible for retrieving and supplying the 
/// OpenAPI Specification (OAS) document to the SDK's internal resolvers.
/// 
/// By exposing this interface, developers are completely abstracted away from 
/// where the OAS originates. While the default implementation loads it from the local disk, 
/// custom implementations can easily fetch the specification from a cloud storage bucket, 
/// an internal URL, a database, or dynamically generate it on-the-fly.
/// </summary>
public interface IOpenApiSpecProvider
{
    /// <summary>
    /// Asynchronously retrieves the OpenAPI specification and returns it as a <see cref="Stream"/>.
    /// The specification can be formatted in either JSON or YAML.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token that can be used to cancel the fetch operation if it involves a long-running network request.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> that represents the asynchronous fetch operation. 
    /// The task result contains the <see cref="Stream"/> of the OpenAPI document. 
    /// The consumer (usually the resolver) assumes responsibility for disposing the stream after use.
    /// </returns>
    Task<Stream> GetOpenApiSpecAsync(CancellationToken cancellationToken = default);
}
