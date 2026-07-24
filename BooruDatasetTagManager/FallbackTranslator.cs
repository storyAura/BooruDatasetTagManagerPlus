using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BooruDatasetTagManager
{
    public class FallbackTranslator : AbstractTranslator
    {
        private readonly List<AbstractTranslator> translators;
        private readonly TimeSpan timeout;

        public FallbackTranslator(IEnumerable<AbstractTranslator> translators, TimeSpan timeout)
            : base(TranslationService.ChineseTranslate)
        {
            if (translators == null)
                throw new ArgumentNullException(nameof(translators));

            this.translators = translators.Where(item => item != null).ToList();
            this.timeout = timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(5) : timeout;
        }

        public static FallbackTranslator Create(IEnumerable<TranslationService> services, int timeoutSeconds)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            return new FallbackTranslator(
                services.Distinct().Select(AbstractTranslator.Create),
                TimeSpan.FromSeconds(timeoutSeconds <= 0 ? 5 : timeoutSeconds));
        }

        // Extra time a canceled attempt gets to acknowledge its token before
        // it is abandoned the old way (covers token-ignoring translators).
        private static readonly TimeSpan GraceWindow = TimeSpan.FromSeconds(2);

        public override async Task<string> TranslateAsync(string text, string fromLang, string toLang, CancellationToken cancellationToken = default)
        {
            foreach (var translator in translators)
            {
                string result = await TryTranslateAsync(translator, text, fromLang, toLang, cancellationToken);
                if (!string.IsNullOrWhiteSpace(result))
                    return result;
            }

            return string.Empty;
        }

        private async Task<string> TryTranslateAsync(AbstractTranslator translator, string text, string fromLang, string toLang, CancellationToken outerToken)
        {
            // Linked per-attempt source: on timeout the underlying HTTP call
            // is actually canceled instead of left running in the background
            // while the chain moves on to the next provider.
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
            attempt.CancelAfter(timeout);
            try
            {
                Task<string> translateTask = translator.TranslateAsync(text, fromLang, toLang, attempt.Token);
                Task completed = await Task.WhenAny(translateTask, Task.Delay(timeout + GraceWindow));
                if (!ReferenceEquals(completed, translateTask))
                    return string.Empty;

                return await translateTask ?? string.Empty;
            }
            catch (OperationCanceledException) when (outerToken.IsCancellationRequested)
            {
                // The caller canceled the whole translation: don't fall
                // through to the next provider.
                throw;
            }
            catch
            {
                return string.Empty;
            }
        }

        public override void Dispose()
        {
            foreach (var translator in translators)
                translator.Dispose();
        }
    }
}
