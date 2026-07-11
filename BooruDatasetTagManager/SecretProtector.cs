using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Encrypts small secrets (e.g. API keys) at rest using Windows DPAPI
    /// (<see cref="ProtectedData"/>) bound to the current user account.
    /// Encrypted values are tagged with a marker prefix so plaintext values
    /// written by older versions are transparently migrated on next save.
    /// </summary>
    public static class SecretProtector
    {
        // Marker that identifies a DPAPI-protected, base64-encoded payload.
        private const string ProtectedPrefix = "dpapi:";

        /// <summary>
        /// True when at least one stored secret could not be decrypted this
        /// session (settings copied from another machine/user account or a
        /// corrupted payload). The UI uses this to tell the user to re-enter
        /// keys instead of silently showing empty fields.
        /// </summary>
        public static bool UnprotectFailureOccurred { get; private set; }

        /// <summary>
        /// Returns a storable representation of <paramref name="plainText"/>.
        /// On Windows the value is DPAPI-encrypted and prefixed; if encryption
        /// is unavailable the original text is returned unchanged so the
        /// feature never blocks saving settings.
        /// </summary>
        public static string Protect(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return plainText;

            // Already protected: keep as-is (idempotent).
            if (plainText.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
                return plainText;

            if (!OperatingSystem.IsWindows())
                return plainText;

            try
            {
                byte[] data = Encoding.UTF8.GetBytes(plainText);
                byte[] encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
                return ProtectedPrefix + Convert.ToBase64String(encrypted);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"SecretProtector.Protect failed: {ex}");
                return plainText;
            }
        }

        /// <summary>
        /// Reverses <see cref="Protect"/>. Values without the marker prefix are
        /// treated as legacy plaintext and returned unchanged.
        /// </summary>
        public static string Unprotect(string storedValue)
        {
            if (string.IsNullOrEmpty(storedValue))
                return storedValue;

            if (!storedValue.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
                return storedValue; // legacy plaintext

            if (!OperatingSystem.IsWindows())
                return string.Empty;

            try
            {
                string base64 = storedValue.Substring(ProtectedPrefix.Length);
                byte[] encrypted = Convert.FromBase64String(base64);
                byte[] data = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch (Exception ex)
            {
                // Wrong user / corrupted blob: fail closed rather than leaking ciphertext.
                Trace.WriteLine($"SecretProtector.Unprotect failed: {ex}");
                UnprotectFailureOccurred = true;
                return string.Empty;
            }
        }
    }
}
