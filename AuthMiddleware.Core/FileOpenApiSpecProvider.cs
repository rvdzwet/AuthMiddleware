using Microsoft.Extensions.Options;

namespace AuthMiddleware.Core;

/// <summary>
/// A default, built-in implementation of the <see cref="IOpenApiSpecProvider"/> interface 
/// that reads the OpenAPI Specification (OAS) from a local file system path.
/// This provider is registered automatically by <c>AddAuthContext</c> unless a custom 
/// provider has already been registered in the dependency injection container.
/// </summary>
public class FileOpenApiSpecProvider : IOpenApiSpecProvider
{
    private readonly AuthContextOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileOpenApiSpecProvider"/> class.
    /// </summary>
    /// <param name="options">
    /// The <see cref="IOptions{AuthContextOptions}"/> containing the configured 
    /// <see cref="AuthContextOptions.OpenApiSpecPath"/> pointing to the local OAS file.
    /// </param>
    public FileOpenApiSpecProvider(IOptions<AuthContextOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Asynchronously opens and returns a readable stream for the configured OpenAPI Specification file.
    /// The stream is returned without being fully buffered into memory, allowing the downstream 
    /// <see cref="IOpenApiOperationResolver"/> to parse it efficiently.
    /// </summary>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation, containing the file <see cref="Stream"/>.</returns>
    /// <exception cref="FileNotFoundException">
    /// Thrown when the configured path in <see cref="AuthContextOptions.OpenApiSpecPath"/> is null, 
    /// empty, or does not point to an existing file on the disk.
    /// </exception>
    public Task<Stream> GetOpenApiSpecAsync(CancellationToken cancellationToken = default)
    {
        // Validate that a path was provided and that the file actually exists on the filesystem.
        if (string.IsNullOrWhiteSpace(_options.OpenApiSpecPath) || !File.Exists(_options.OpenApiSpecPath))
        {
            throw new FileNotFoundException($"OpenAPI Specification file not found at '{_options.OpenApiSpecPath}'. Please configure OpenApiSpecPath or provide a custom IOpenApiSpecProvider.");
        }

        // Open a read-only stream to the file.
        // The caller is responsible for disposing of this stream once parsing is complete.
        Stream stream = File.OpenRead(_options.OpenApiSpecPath);
        return Task.FromResult(stream);
    }
}
