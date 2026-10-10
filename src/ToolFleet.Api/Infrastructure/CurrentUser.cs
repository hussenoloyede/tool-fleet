namespace ToolFleet.Api.Infrastructure;

/// <summary>Who is making the change, for the audit log.</summary>
public interface ICurrentUser
{
    string Name { get; }
}

/// <summary>
/// Reads the signed-in user from the request. Until Entra ID login is added, requests are
/// "anonymous"; work done outside a request (startup seeding) is "system".
/// </summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string Name =>
        accessor.HttpContext is { } context
            ? context.User.Identity?.Name ?? "anonymous"
            : "system";
}
