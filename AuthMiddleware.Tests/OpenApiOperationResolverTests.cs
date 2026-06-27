using AuthMiddleware.Core;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthMiddleware.Tests;

public class OpenApiOperationResolverTests
{
    private readonly string _tempSpecPath;

    public OpenApiOperationResolverTests()
    {
        _tempSpecPath = Path.GetTempFileName();
        var openApiContent = @"openapi: 3.0.1
info:
  title: Test API
  version: v1
paths:
  /api/users/{id}:
    get:
      operationId: GetUserById
      responses:
        '200':
          description: Success
  /api/orders:
    post:
      operationId: CreateOrder
      responses:
        '201':
          description: Created
";
        File.WriteAllText(_tempSpecPath, openApiContent);
    }

    [Fact]
    public async Task ResolveOperationId_ReturnsCorrectId_ForExactMatch()
    {
        var resolver = CreateResolver();
        var result = await resolver.ResolveOperationIdAsync("POST", "/api/orders");
        Assert.Equal("CreateOrder", result);
    }

    [Fact]
    public async Task ResolveOperationId_ReturnsCorrectId_ForPathParameter()
    {
        var resolver = CreateResolver();
        var result = await resolver.ResolveOperationIdAsync("GET", "/api/users/123");
        Assert.Equal("GetUserById", result);
    }

    [Fact]
    public async Task ResolveOperationId_ReturnsNull_ForUnknownPath()
    {
        var resolver = CreateResolver();
        var result = await resolver.ResolveOperationIdAsync("GET", "/api/unknown");
        Assert.Null(result);
    }

    private OpenApiOperationResolver CreateResolver()
    {
        var options = Options.Create(new AuthContextOptions { OpenApiSpecPath = _tempSpecPath });
        var provider = new FileOpenApiSpecProvider(options);
        return new OpenApiOperationResolver(provider);
    }
}

