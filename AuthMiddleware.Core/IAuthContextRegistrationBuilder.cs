using Microsoft.Extensions.DependencyInjection;

namespace AuthMiddleware.Core;

/// <summary>
/// A fluent builder interface used during dependency injection configuration.
/// This provides developers with a highly readable, chainable API (e.g., <c>.MapOperation()</c>)
/// for registering their <see cref="IOperationHandler"/> implementations against specific OpenAPI Operation IDs.
/// </summary>
public interface IAuthContextRegistrationBuilder
{
    /// <summary>
    /// Gets the underlying <see cref="IServiceCollection"/> where the services are registered.
    /// </summary>
    IServiceCollection Services { get; }
}
