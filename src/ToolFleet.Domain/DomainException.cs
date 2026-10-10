namespace ToolFleet.Domain;

/// <summary>
/// Thrown when a request breaks a business rule (for example, promoting a halted rollout).
/// The API turns this into a 409 Conflict.
/// </summary>
public sealed class DomainException(string message) : Exception(message);
