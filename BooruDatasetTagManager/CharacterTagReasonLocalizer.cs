using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace BooruDatasetTagManager
{
    public sealed class CharacterTagLocalizedReason
    {
        public string Text { get; set; } = string.Empty;
        public bool UsedFallback { get; set; }
    }

    public sealed class CharacterTagReasonLocalizer
    {
        private readonly string targetLanguage;
        private readonly Func<string, string, string, CancellationToken, Task<string>> translateAsync;
        private readonly ConcurrentDictionary<string, Lazy<Task<CharacterTagLocalizedReason>>> cache =
            new ConcurrentDictionary<string, Lazy<Task<CharacterTagLocalizedReason>>>(StringComparer.Ordinal);

        public CharacterTagReasonLocalizer(
            string targetLanguage,
            Func<string, string, string, CancellationToken, Task<string>> translateAsync)
        {
            this.targetLanguage = string.IsNullOrWhiteSpace(targetLanguage) ? "en-US" : targetLanguage;
            this.translateAsync = translateAsync ?? throw new ArgumentNullException(nameof(translateAsync));
        }

        public bool RequiresTranslation => !targetLanguage.StartsWith("en", StringComparison.OrdinalIgnoreCase);

        public Task<CharacterTagLocalizedReason> LocalizeAsync(
            string reason,
            CancellationToken cancellationToken = default)
        {
            string original = reason ?? string.Empty;
            if (!RequiresTranslation || string.IsNullOrWhiteSpace(original))
            {
                return Task.FromResult(new CharacterTagLocalizedReason
                {
                    Text = original,
                    UsedFallback = false
                });
            }

            Lazy<Task<CharacterTagLocalizedReason>> lazy = cache.GetOrAdd(original, text =>
                new Lazy<Task<CharacterTagLocalizedReason>>(
                    () => TranslateCoreAsync(text, cancellationToken),
                    LazyThreadSafetyMode.ExecutionAndPublication));
            return lazy.Value;
        }

        private async Task<CharacterTagLocalizedReason> TranslateCoreAsync(
            string original,
            CancellationToken cancellationToken)
        {
            try
            {
                string translated = await translateAsync(original, "en", targetLanguage, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(translated))
                    return new CharacterTagLocalizedReason { Text = translated, UsedFallback = false };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
            }

            return new CharacterTagLocalizedReason { Text = original, UsedFallback = true };
        }
    }
}
