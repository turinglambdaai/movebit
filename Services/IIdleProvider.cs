namespace MoveBit.Services;

/// <summary>
/// Reports how long the user has been idle (no keyboard/mouse input anywhere in the session).
/// Returns null when the platform has no idle detection — callers should then treat the
/// user as always active and rely on natural time only.
/// </summary>
public interface IIdleProvider
{
    TimeSpan? GetIdleTime();
}

/// Fallback for platforms without idle detection (currently macOS/Linux).
public sealed class NullIdleProvider : IIdleProvider
{
    public TimeSpan? GetIdleTime() => null;
}
