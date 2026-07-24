using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BooruDatasetTagManager
{
    public abstract class AbstractTranslator : IDisposable
    {
        public TranslationService Service { get; set; }
        public AbstractTranslator(TranslationService service)
        {
            Service = service;
        }

        public static AbstractTranslator Create(TranslationService service)
        {
            switch (service)
            {
                case TranslationService.GoogleTranslate:
                    {
                        return new GoogleTranslator();
                    }
                case TranslationService.ChineseTranslate:
                    {
                        return new ChineseTranslator();
                    }
                case TranslationService.MyMemoryTranslate:
                    {
                        return new MyMemoryTranslator();
                    }
                case TranslationService.GoogleJsonTranslate:
                    {
                        return new GoogleJsonTranslator();
                    }
                default:
                    throw new NotImplementedException("Translation service not implemented");
            }
        }

        /// <summary>
        /// TRANS-01b: the token flows into the underlying HTTP call so a
        /// fallback timeout (or caller cancel) really cancels the request
        /// instead of leaving it running in the background.
        /// </summary>
        public virtual Task<string> TranslateAsync(string text, string fromLang, string toLang, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string>(null);
        }

        public abstract void Dispose();
    }
}
