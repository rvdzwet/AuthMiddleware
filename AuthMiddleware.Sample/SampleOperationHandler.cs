using AuthMiddleware.Core;
using Microsoft.AspNetCore.Http;

namespace AuthMiddleware.Sample;

public class SampleOperationHandler : IOperationHandler
{
    public Task<IAuthContext?> BuildContextAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        // For demonstration, we allow all requests and return a dummy user context.
        var model = new SampleAuthContext
        {
            UserId = "user-123",
            Roles = new[] { "Admin", "User" },
            IsAuthorized = true
        };

        return Task.FromResult<IAuthContext?>(model);
    }
}
