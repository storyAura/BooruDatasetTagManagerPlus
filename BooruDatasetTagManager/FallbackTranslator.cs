using System;
using System.Collections.Generic;
using System.Linq;
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

        public override async Task<string> TranslateAsync(string text, string fromLang, string toLang)
        {
            foreach (var translator in translators)
            {
                string result = await TryTranslateAsync(translator, text, fromLang, toLang);
                if (!string.IsNullOrWhiteSpace(result))
                    return result;
            }

            return string.Empty;
        }

        private async Task<string> TryTranslateAsync(AbstractTranslator translator, string text, string fromLang, string toLang)
        {
            try
            {
                Task<string> translateTask = translator.TranslateAsync(text, fromLang, toLang);
                Task completed = await Task.WhenAny(translateTask, Task.Delay(timeout));
                if (!ReferenceEquals(completed, translateTask))
                    return string.Empty;

                return await translateTask ?? string.Empty;
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
