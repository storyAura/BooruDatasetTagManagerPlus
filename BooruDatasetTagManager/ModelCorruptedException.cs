using System;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Thrown when a locally cached model fails its integrity check on load
    /// because the file is corrupt or incomplete. The service deletes the bad
    /// file(s) before throwing, so the next attempt re-downloads a clean copy.
    /// </summary>
    public sealed class ModelCorruptedException : Exception
    {
        public ModelCorruptedException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
