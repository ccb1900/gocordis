using Industrial.Contracts;

namespace Industrial.Core;

/// <summary>Guards reuse of a committed source stage. A resumed instance must
/// not silently transform a file that changed after the stage was committed.</summary>
public static class StagedFileIdentity
{
    public static void Verify(BatchFileRef staged, FileIdentity current)
    {
        if (!string.Equals(current.Key, staged.IdentityKey, StringComparison.Ordinal) ||
            !string.Equals(current.ContentHash, staged.ContentHash, StringComparison.Ordinal) ||
            (staged.Length > 0 && current.Length != staged.Length))
        {
            throw new InvalidOperationException(
                $"staged source file '{staged.IdentityKey}' changed on disk; refusing to reuse its stage");
        }
    }
}
