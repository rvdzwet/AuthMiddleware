using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace AuthMiddleware.Core;

/// <summary>
/// Provides extension methods for registering and configuring the Auth Context Middleware
/// in an ASP.NET Core application's dependency injection container and request pipeline.
/// </summary>
public static class AuthContextExtensions
{
    /// <summary>
    /// Registers the Auth Context Middleware core services, including the OpenAPI operation resolver 
    /// and the default specification provider.
    /// 
    /// This method returns an <see cref="IAuthContextRegistrationBuilder"/> which allows developers 
    /// to fluently map specific <see cref="IOperationHandler"/> implementations to OpenAPI operation IDs.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="configureOptions">An action to configure the <see cref="AuthContextOptions"/>.</param>
    /// <returns>A fluent builder for mapping specific operation handlers.</returns>
    public static IAuthContextRegistrationBuilder AddAuthContext(
        this IServiceCollection services,
        Action<AuthContextOptions> configureOptions)
    {
        // Register the provided configuration action against the AuthContextOptions
        // so that they can be resolved via IOptions<AuthContextOptions> in the middleware.
        services.Configure(configureOptions);
        
        // Register the default File-based OpenAPI Spec Provider using TryAddSingleton.
        // This allows consumers to register their own custom IOpenApiSpecProvider 
        // before calling AddAuthContext, preventing their custom implementation from being overwritten here.
        Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddSingleton<IOpenApiSpecProvider, FileOpenApiSpecProvider>(services);
        
        // Register the OpenAPI operation resolver as a singleton. It's safe as a singleton
        // because it uses lazy initialization and SemaphoreSlim to securely parse the OAS once.
        services.AddSingleton<IOpenApiOperationResolver, OpenApiOperationResolver>();
        
        return new AuthContextRegistrationBuilder(services);
    }

    /// <summary>
    /// Maps a specific <see cref="IOperationHandler"/> to an OpenAPI Operation ID.
    /// When the middleware resolves this operation ID, it will execute this handler to build the context.
    /// </summary>
    /// <typeparam name="THandler">The implementation type of the operation handler.</typeparam>
    /// <param name="builder">The fluent registration builder.</param>
    /// <param name="operationId">The exact OpenAPI Operation ID (e.g., "CreateOrder") to bind this handler to.</param>
    /// <returns>The builder instance, allowing for chainable registrations.</returns>
    public static IAuthContextRegistrationBuilder MapOperation<THandler>(
        this IAuthContextRegistrationBuilder builder,
        string operationId)
        where THandler : class, IOperationHandler
    {
        // Leverage .NET 8+ Keyed Dependency Injection to register multiple IOperationHandlers,
        // using the literal string 'operationId' as the lookup key.
        builder.Services.AddKeyedTransient<IOperationHandler, THandler>(operationId);
        
        return builder;
    }

    /// <summary>
    /// Adds the Auth Context Middleware to the ASP.NET Core request pipeline.
    /// This intercepts incoming requests and dynamically invokes the correct operation handler.
    /// </summary>
    /// <param name="builder">The <see cref="IApplicationBuilder"/> to add the middleware to.</param>
    /// <returns>The <see cref="IApplicationBuilder"/> so that additional calls can be chained.</returns>
    public static IApplicationBuilder UseAuthContext(this IApplicationBuilder builder)
    {
        // Insert the AuthContextMiddleware into the pipeline.
        return builder.UseMiddleware<AuthContextMiddleware>();
    }

    /// <summary>
    /// Internal implementation of the <see cref="IAuthContextRegistrationBuilder"/>.
    /// </summary>
    private class AuthContextRegistrationBuilder : IAuthContextRegistrationBuilder
    {
        public IServiceCollection Services { get; }

        public AuthContextRegistrationBuilder(IServiceCollection services)
        {
            Services = services;
        }
    }
}
