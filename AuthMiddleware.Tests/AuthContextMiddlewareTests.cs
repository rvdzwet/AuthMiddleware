using System.Text.Json;
using AuthMiddleware.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthMiddleware.Tests;

public class TestAuthContext : IAuthContext
{
    public string OperationId { get; set; } = string.Empty;
    public bool Success { get; set; }
}

public class TestOperationHandler : IOperationHandler
{
    public Task<IAuthContext?> BuildContextAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        // Simple mock behavior based on header for testing
        if (context.Request.Headers.TryGetValue("X-Test-Action", out var action) && action == "Deny")
        {
            return Task.FromResult<IAuthContext?>(null);
        }

        return Task.FromResult<IAuthContext?>(new TestAuthContext { Success = true });
    }
}

public class StubResolver : IOpenApiOperationResolver
{
    public Task<string?> ResolveOperationIdAsync(string method, string path, CancellationToken cancellationToken = default)
    {
        if (path == "/mapped") return Task.FromResult<string?>("MappedOp");
        if (path == "/unmapped") return Task.FromResult<string?>("UnmappedOp");
        return Task.FromResult<string?>(null);
    }
}

// A simple mock for IProblemDetailsService that writes directly to the response for tests
public class MockProblemDetailsService : IProblemDetailsService
{
    public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
    {
        var response = context.HttpContext.Response;
        response.ContentType = "application/problem+json";
        var json = JsonSerializer.Serialize(context.ProblemDetails);
        response.WriteAsync(json).Wait();
        return ValueTask.FromResult(true);
    }

    public ValueTask WriteAsync(ProblemDetailsContext context)
    {
        TryWriteAsync(context).AsTask().Wait();
        return ValueTask.CompletedTask;
    }
}

public class AuthContextMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_PassesThrough_IfPathDoesNotMatch()
    {
        var options = Options.Create(new AuthContextOptions { EndpointPath = "/_auth" });
        var middleware = new AuthContextMiddleware(
            next: ctx => { ctx.Response.StatusCode = 202; return Task.CompletedTask; },
            options,
            new StubResolver(),
            NullLogger<AuthContextMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = "/other";
        
        var services = new ServiceCollection();
        services.AddSingleton<IProblemDetailsService, MockProblemDetailsService>();
        context.RequestServices = services.BuildServiceProvider();

        await middleware.InvokeAsync(context, context.RequestServices.GetRequiredService<IProblemDetailsService>());

        Assert.Equal(202, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_Returns404_IfOperationNotFound()
    {
        var options = Options.Create(new AuthContextOptions { EndpointPath = "/_auth" });
        var middleware = new AuthContextMiddleware(
            next: ctx => Task.CompletedTask,
            options,
            new StubResolver(),
            NullLogger<AuthContextMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = "/_auth";
        context.Request.Headers["X-Forwarded-Path"] = "/unknown";
        context.Response.Body = new MemoryStream();
        
        var services = new ServiceCollection();
        services.AddSingleton<IProblemDetailsService, MockProblemDetailsService>();
        context.RequestServices = services.BuildServiceProvider();

        await middleware.InvokeAsync(context, context.RequestServices.GetRequiredService<IProblemDetailsService>());

        Assert.Equal(404, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_Returns403_IfHandlerReturnsNull()
    {
        var options = Options.Create(new AuthContextOptions { EndpointPath = "/_auth" });
        var middleware = new AuthContextMiddleware(
            next: ctx => Task.CompletedTask,
            options,
            new StubResolver(),
            NullLogger<AuthContextMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = "/_auth";
        context.Request.Headers["X-Forwarded-Path"] = "/mapped";
        context.Request.Headers["X-Test-Action"] = "Deny";
        context.Response.Body = new MemoryStream();
        
        var services = new ServiceCollection();
        // Register the fluent builder services
        services.AddAuthContext(opt => opt.EndpointPath = "/_auth")
                .MapOperation<TestOperationHandler>("MappedOp");
        services.AddSingleton<IProblemDetailsService, MockProblemDetailsService>();
        context.RequestServices = services.BuildServiceProvider();

        await middleware.InvokeAsync(context, context.RequestServices.GetRequiredService<IProblemDetailsService>());

        Assert.Equal(403, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_Returns200AndSetsOperationId_IfSuccessful()
    {
        var options = Options.Create(new AuthContextOptions { EndpointPath = "/_auth" });
        var middleware = new AuthContextMiddleware(
            next: ctx => Task.CompletedTask,
            options,
            new StubResolver(),
            NullLogger<AuthContextMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = "/_auth";
        context.Request.Headers["X-Forwarded-Path"] = "/mapped";
        context.Response.Body = new MemoryStream();
        
        var services = new ServiceCollection();
        services.AddAuthContext(opt => opt.EndpointPath = "/_auth")
                .MapOperation<TestOperationHandler>("MappedOp");
        services.AddSingleton<IProblemDetailsService, MockProblemDetailsService>();
        context.RequestServices = services.BuildServiceProvider();

        await middleware.InvokeAsync(context, context.RequestServices.GetRequiredService<IProblemDetailsService>());

        Assert.Equal(200, context.Response.StatusCode);
        
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var json = await reader.ReadToEndAsync();
        
        var result = JsonSerializer.Deserialize<TestAuthContext>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(result);
        Assert.Equal("MappedOp", result!.OperationId);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task InvokeAsync_FallsBackToDefaultContext_IfNoHandlerMapped()
    {
        var options = Options.Create(new AuthContextOptions { EndpointPath = "/_auth" });
        var middleware = new AuthContextMiddleware(
            next: ctx => Task.CompletedTask,
            options,
            new StubResolver(),
            NullLogger<AuthContextMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = "/_auth";
        context.Request.Headers["X-Forwarded-Path"] = "/unmapped"; // Resolves to "UnmappedOp"
        context.Response.Body = new MemoryStream();
        
        var services = new ServiceCollection();
        services.AddAuthContext(opt => opt.EndpointPath = "/_auth")
                .MapOperation<TestOperationHandler>("MappedOp"); // Specifically mapped to something else
        services.AddSingleton<IProblemDetailsService, MockProblemDetailsService>();
        context.RequestServices = services.BuildServiceProvider();

        await middleware.InvokeAsync(context, context.RequestServices.GetRequiredService<IProblemDetailsService>());

        Assert.Equal(200, context.Response.StatusCode);
        
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var json = await reader.ReadToEndAsync();
        
        var result = JsonSerializer.Deserialize<DefaultAuthContext>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(result);
        Assert.Equal("UnmappedOp", result!.OperationId);
    }
}
