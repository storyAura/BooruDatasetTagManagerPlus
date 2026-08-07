using System.Security.Cryptography;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class SecretProtectorTests
{
    [Fact]
    public void Protect_fails_closed_without_returning_plaintext()
    {
        try
        {
            SecretProtector.ProtectCoreForTests = _ => throw new CryptographicException("forced");
            var ex = Assert.Throws<CryptographicException>(() => SecretProtector.Protect("sk-must-not-leak"));
            Assert.Contains("encrypt", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(SecretProtector.ProtectFailureOccurred);
        }
        finally
        {
            SecretProtector.ProtectCoreForTests = null;
        }
    }

    [Fact]
    public void SaveSettings_aborts_on_protect_failure_and_keeps_prior_encrypted_file()
    {
        using var temp = new TemporaryDirectory();
        var settings = new AppSettings(temp.Path);
        settings.OpenAiAutoTagger.ApiKey = "sk-original-secret";
        settings.SaveSettings();

        string settingsPath = Path.Combine(temp.Path, "settings.json");
        string before = File.ReadAllText(settingsPath);
        Assert.DoesNotContain("sk-original-secret", before, StringComparison.Ordinal);
        Assert.Contains("dpapi:", before, StringComparison.Ordinal);

        settings.Language = "en-US";
        settings.OpenAiAutoTagger.ApiKey = "sk-new-secret-should-not-persist";
        try
        {
            SecretProtector.ProtectCoreForTests = _ => throw new CryptographicException("forced");
            settings.SaveSettings();
        }
        finally
        {
            SecretProtector.ProtectCoreForTests = null;
        }

        Assert.True(SecretProtector.ProtectFailureOccurred);
        Assert.Equal(before, File.ReadAllText(settingsPath));
        Assert.DoesNotContain("sk-new-secret-should-not-persist", File.ReadAllText(settingsPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Protect_then_unprotect_roundtrips_plaintext()
    {
        const string secret = "sk-test-1234567890";

        string stored = SecretProtector.Protect(secret);

        Assert.StartsWith("dpapi:", stored, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, stored, StringComparison.Ordinal);
        Assert.Equal(secret, SecretProtector.Unprotect(stored));
    }

    [Fact]
    public void Protect_is_idempotent_for_already_protected_values()
    {
        string stored = SecretProtector.Protect("my-secret");

        Assert.Equal(stored, SecretProtector.Protect(stored));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Protect_and_unprotect_pass_through_null_and_empty(string value)
    {
        Assert.Equal(value, SecretProtector.Protect(value));
        Assert.Equal(value, SecretProtector.Unprotect(value));
    }

    [Fact]
    public void Unprotect_returns_legacy_plaintext_unchanged()
    {
        Assert.Equal("legacy-plain-key", SecretProtector.Unprotect("legacy-plain-key"));
    }

    [Fact]
    public void Unprotect_fails_closed_on_corrupted_payload()
    {
        Assert.Equal(string.Empty, SecretProtector.Unprotect("dpapi:not-valid-base64!!!"));
        Assert.True(SecretProtector.UnprotectFailureOccurred);
    }

    [Fact]
    public void Unprotect_fails_closed_on_forged_ciphertext()
    {
        string forged = "dpapi:" + Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        Assert.Equal(string.Empty, SecretProtector.Unprotect(forged));
    }
}
