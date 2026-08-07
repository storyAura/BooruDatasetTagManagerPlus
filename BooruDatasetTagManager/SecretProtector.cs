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
        /// True when at least one secret could not be encrypted this session.
        /// Callers must abort the settings write so plaintext is never persisted;
        /// the UI warns the user to fix DPAPI / re-enter keys.
        /// </summary>
        public static bool ProtectFailureOccurred { get; private set; }

        /// <summary>
        /// Test-only hook that replaces <see cref="ProtectedData.Protect"/>.
        /// Null in production.
        /// </summary>
        internal static Func<byte[], byte[]> ProtectCoreForTests { get; set; }

        /// <summary>
        /// True when a legacy plaintext secret was read this session. Startup
        /// uses this to re-save settings twice, so both settings.json and its
        /// .bak rotate to the encrypted form.
        /// </summary>
        public static bool LegacyPlaintextLoaded { get; private set; }

        /// <summary>
        /// Returns a storable representation of <paramref name="plainText"/>.
        /// On Windows the value is DPAPI-encrypted and prefixed. Encryption
        /// failures throw after setting <see cref="ProtectFailureOccurred"/> so
        /// callers abort the write and never persist plaintext secrets.
        /// </summary>
        public static string Protect(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return plainText;

            // Already protected: keep as-is (idempotent).
            if (plainText.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
                return plainText;

            if (!OperatingSystem.IsWindows())
            {
                ProtectFailureOccurred = true;
                throw new PlatformNotSupportedException(
                    "Secret encryption requires Windows DPAPI.");
            }

            try
            {
                byte[] data = Encoding.UTF8.GetBytes(plainText);
                byte[] encrypted = ProtectCoreForTests != null
                    ? ProtectCoreForTests(data)
                    : ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
                return ProtectedPrefix + Convert.ToBase64String(encrypted);
            }
            catch (Exception ex) when (ex is not PlatformNotSupportedException)
            {
                Trace.WriteLine($"SecretProtector.Protect failed: {ex}");
                ProtectFailureOccurred = true;
                throw new CryptographicException(
                    "Failed to encrypt secret for storage.", ex);
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
            {
                LegacyPlaintextLoaded = true;
                return storedValue; // legacy plaintext
            }

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
