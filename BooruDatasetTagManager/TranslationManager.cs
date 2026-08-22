using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Translator.Crypto;
using static System.Net.Mime.MediaTypeNames;

namespace BooruDatasetTagManager
{
    public class TranslationManager : IDisposable
    {
        private string _language;
        private string _workDir;
        public List<TransItem> Translations { get; set; }
        private AbstractTranslator translator;
        private string translationFilePath;
        private readonly Dictionary<long, TransItem> translationCache;
        private readonly object cacheSync = new object();
        private readonly SemaphoreSlim translationLocker = new SemaphoreSlim(1, 1);
        // Serializes every append/rewrite of the cache file. translationLocker
        // only guards TranslateAsync, so a public AddTranslationAsync could
        // otherwise append while a rewrite truncates the same file.
        private readonly SemaphoreSlim persistenceLocker = new SemaphoreSlim(1, 1);

        public TranslationManager(
            string toLang,
            TranslationService service,
            string workDir,
            AbstractTranslator translator = null)
        {
            _language = toLang;
            _workDir = workDir;
            Translations = new List<TransItem>();
            translationCache = new Dictionary<long, TransItem>();
            this.translator = translator ?? AbstractTranslator.Create(service);
            translationFilePath = Path.Combine(_workDir, _language + ".txt");
        }

        public void LoadTranslations()
        {
            if (!File.Exists(translationFilePath))
            {
                var sw = File.CreateText(translationFilePath);
                sw.WriteLine("//Translation format: <original>=<translation>");
                sw.Dispose();
                return;
            }
            string[] lines = File.ReadAllLines(translationFilePath);
            foreach (var item in lines)
            {
                if (item.Trim().StartsWith("//"))
                    continue;
                var transItem = TransItem.Create(item);
                if (transItem != null && !Contains(transItem.OrigHash))
                {
                    Translations.Add(transItem);
                    translationCache[transItem.OrigHash] = transItem;
                }
            }
        }

        public bool Contains(string orig)
        {
            return Contains(GetTranslationHash(orig));
        }

        public bool Contains(long hash)
        {
            lock (cacheSync)
            {
                return translationCache.ContainsKey(hash);
            }
        }

        public string GetTranslation(string text)
        {
            return GetTranslation(GetTranslationHash(text));
        }

        public string GetTranslation(long hash)
        {
            lock (cacheSync)
            {
                return translationCache.TryGetValue(hash, out var result)
                    ? result.Trans
                    : null;
            }
        }

        public string GetTranslation(long hash, bool onlyManual)
        {
            if (onlyManual)
            {
                lock (cacheSync)
                {
                    if (!translationCache.TryGetValue(hash, out var result) || !result.IsManual)
                        return null;
                    return result.Trans;
                }
            }
            else
                return GetTranslation(hash);

        }

        public Task<string> GetTranslationAsync(string text)
        {
            return Task.FromResult(GetTranslation(text));
        }
        public void AddTranslation(string orig, string trans, bool isManual)
        {
            AddTranslationAsync(orig, trans, isManual).GetAwaiter().GetResult();
        }

        public Task AddTranslationAsync(string orig, string trans, bool isManual)
        {
            return AddOrUpdateTranslationAsync(orig, trans, isManual, true);
        }

        public Task<string> TranslateAsync(string text)
        {
            return TranslateAsync(text, false);
        }

        /// <summary>
        /// Cache / character catalog / built-in Chinese dictionary only.
        /// Never talks to a network translator, so the all-tags grid can paint
        /// local hits before leftover tags go online.
        /// </summary>
        public string TryGetLocalTranslation(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            TransItem cached = GetTranslationItem(text);
            if (cached != null && cached.IsManual)
                return cached.Trans;

            if (Program.CharacterTagLookup != null)
            {
                string characterTranslation = Program.CharacterTagLookup.GetDisplayTranslation(text);
                if (!string.IsNullOrEmpty(characterTranslation))
                    return characterTranslation;
            }

            if (cached != null)
                return cached.Trans;

            if (ShouldUseBuiltInChineseDictionary())
            {
                var lookup = Program.ChineseTagLookup ?? ChineseTagLookupService.Empty;
                string csvTranslation = lookup.TryGetChineseName(text);
                if (!string.IsNullOrEmpty(csvTranslation))
                    return csvTranslation;
            }

            return string.Empty;
        }

        private bool ShouldUseBuiltInChineseDictionary()
        {
            if (Program.Settings == null || !Program.Settings.UseDanbooruZhCsvBeforeTranslation)
                return false;

            if (ChineseTagLookupService.IsChineseLanguage(_language))
                return true;

            return ChineseTagLookupService.IsChineseLanguage(Program.Settings.Language);
        }

        public async Task<string> TranslateAsync(string text, bool forceRefresh, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            await translationLocker.WaitAsync(cancellationToken);
            try
            {
                TransItem cached = GetTranslationItem(text);
                // Manual translations always win.
                if (cached != null && cached.IsManual)
                    return cached.Trans;

                if (!forceRefresh)
                {
                    string local = TryGetLocalTranslation(text);
                    if (!string.IsNullOrEmpty(local))
                    {
                        if (cached == null || !string.Equals(cached.Trans, local, StringComparison.Ordinal))
                            await AddOrUpdateTranslationAsync(text, local, false, false);
                        return local;
                    }
                }

                string result = await translator.TranslateAsync(text, "en", _language, cancellationToken);
                if (!string.IsNullOrEmpty(result))
                    await AddOrUpdateTranslationAsync(text, result, false, forceRefresh);
                return result ?? string.Empty;
            }
            finally
            {
                translationLocker.Release();
            }
        }

        private TransItem GetTranslationItem(string text)
        {
            long hash = GetTranslationHash(text);
            lock (cacheSync)
            {
                translationCache.TryGetValue(hash, out var result);
                return result;
            }
        }

        private async Task AddOrUpdateTranslationAsync(string orig, string trans, bool isManual, bool allowOverwrite)
        {
            TransItem item = new TransItem(orig, trans, isManual);
            bool rewriteFile = false;

            lock (cacheSync)
            {
                if (translationCache.TryGetValue(item.OrigHash, out var existing))
                {
                    if (!allowOverwrite || (existing.IsManual && !isManual))
                        return;

                    existing.Trans = trans;
                    rewriteFile = true;
                }
                else
                {
                    translationCache[item.OrigHash] = item;
                    Translations.Add(item);
                }
            }

            await persistenceLocker.WaitAsync().ConfigureAwait(false);
            try
            {
                if (rewriteFile)
                    await RewriteTranslationsFileAsync().ConfigureAwait(false);
                else
                    await File.AppendAllTextAsync(translationFilePath, $"{(isManual ? "*" : "")}{orig}={trans}\r\n", Encoding.UTF8).ConfigureAwait(false);
            }
            finally
            {
                persistenceLocker.Release();
            }
        }

        private Task RewriteTranslationsFileAsync()
        {
            // The snapshot is taken inside the persistence lock, so a rewrite
            // always persists at least every entry that was appended before it.
            List<TransItem> snapshot;
            lock (cacheSync)
            {
                snapshot = Translations.ToList();
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("//Translation format: <original>=<translation>");
            foreach (var item in snapshot)
                builder.AppendLine($"{(item.IsManual ? "*" : "")}{item.Orig}={item.Trans}");

            // Atomic replace: a crash/disk-full mid-rewrite must not leave the
            // cache file truncated.
            return Task.Run(() => SafeFile.WriteAllText(translationFilePath, builder.ToString(), Encoding.UTF8));
        }

        private void AddToCache(TransItem item)
        {
            lock (cacheSync)
            {
                if (translationCache.ContainsKey(item.OrigHash))
                    return;
                translationCache[item.OrigHash] = item;
                Translations.Add(item);
            }
        }

        private static long GetTranslationHash(string text)
        {
            return text.ToLowerInvariant().Trim().GetHash();
        }

        public void Dispose()
        {
            translator?.Dispose();
            translationLocker.Dispose();
            persistenceLocker.Dispose();
        }

        public class TransItem
        {
            public string Orig { get; private set; }
            public string Trans {get; set; }
            public long OrigHash { get; private set; }
            public bool IsManual { get; private set; }

            public TransItem(string orig, string trans, bool isManual)
            {
                Orig = orig;
                Trans = trans;
                OrigHash = GetTranslationHash(orig);
                IsManual = isManual;
            }

            public static TransItem Create(string text)
            {
                bool manual = false;
                if (text.StartsWith("*"))
                {
                    text = text.Substring(1);
                    manual = true;
                }
                int index = text.LastIndexOf('=');
                if (index == -1)
                    return null;
                string orig = text.Substring(0, index).Trim();
                string trans = text.Substring(index + 1).Trim();
                return new TransItem(orig, trans, manual);
            }


            public TransItem() { }
        }
    }
}
