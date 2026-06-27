using AuthMiddleware.Core;

namespace AuthMiddleware.Sample;

public class SampleAuthContext : IAuthContext
{
    public string UserId { get; set; } = string.Empty;
    public string[] Roles { get; set; } = Array.Empty<string>();
    public string OperationId { get; set; } = string.Empty;
    public bool IsAuthorized { get; set; }
}

