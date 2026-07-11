using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BooruDatasetTagManager
{
    public class LanguageManager
    {
        public const string defaultLang = "en-US";
        public Dictionary<string, Dictionary<string, string>> Langs { get; set; }
        public LanguageManager()
        {
            // Startup path: a missing folder, duplicate key or read error here
            // used to be an unrecoverable crash before any window appeared.
            Langs = new Dictionary<string, Dictionary<string, string>>();
            string langDir = Path.Combine(Program.AppPath, "Languages");
            if (!Directory.Exists(langDir))
            {
                Trace.WriteLine($"LanguageManager: directory not found: {langDir}");
                return;
            }
            string[] files = Directory.GetFiles(langDir, "*.txt", SearchOption.TopDirectoryOnly);
            foreach (string file in files)
            {
                try
                {
                    LoadLanguageFromFile(file);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"LanguageManager: failed to load '{file}': {ex}");
                }
            }
            FixOldLangFilesFromDefault();
        }

        private void LoadLanguageFromFile(string filename)
        {
            Dictionary<string, string> langData = new Dictionary<string, string>();
            string[] fileData = File.ReadAllLines(filename, Encoding.UTF8);
            foreach (string line in fileData)
            {
                int spIndex = line.IndexOf('=');
                if (spIndex == -1)
                    continue;
                // Indexer instead of Add: a duplicated key in a hand-edited file
                // must not abort loading the whole language.
                langData[line.Substring(0, spIndex).Trim()] = line.Substring(spIndex + 1);
            }
            Langs[Path.GetFileNameWithoutExtension(filename)] = langData;
        }

        private void FixOldLangFilesFromDefault()
        {
            if (!Langs.ContainsKey(defaultLang))
                return;
            List<string> langToSave = new List<string>();
            foreach (string index in Langs[defaultLang].Keys)
            {
                foreach (var lang in Langs.Keys)
                {
                    if (lang == defaultLang)
                        continue;
                    if (!Langs[lang].ContainsKey(index))
                    {
                        Langs[lang].Add(index, Langs[defaultLang][index]);
                        if(!langToSave.Contains(lang))
                            langToSave.Add(lang);
                    }
                }
            }
            foreach (var toSave in langToSave)
                SaveLangFile(toSave);
        }

        private void SaveLangFile(string lang)
        {
            string filePath = Path.Combine(Program.AppPath, "Languages", lang + ".txt");
            try
            {
                using StreamWriter sw = new StreamWriter(filePath, false, Encoding.UTF8);
                foreach (string key in Langs[lang].Keys)
                {
                    sw.WriteLine(key + "=" + Langs[lang][key]);
                }
            }
            catch (Exception ex)
            {
                // Read-only install dir: keep the merged keys in memory only.
                Trace.WriteLine($"LanguageManager: failed to update '{filePath}': {ex}");
            }
        }
    }
}
