namespace MoveBit;

/// Build-flavor flags. The public MIT build never defines PRO; the private
/// overlay repo (movebit-pro) compiles these same sources with PRO defined,
/// adding the paid sync feature. Everything sync-related must stay behind
/// either this flag or the private repo — never both halves in public.
public static class BuildInfo
{
#if PRO
    public const bool IsPro = true;
#else
    public const bool IsPro = false;
#endif
}
