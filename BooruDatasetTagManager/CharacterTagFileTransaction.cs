using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BooruDatasetTagManager
{
    public sealed class CharacterTagFileChange
    {
        public CharacterTagFileChange(string targetPath, string newContent)
        {
            TargetPath = targetPath;
            NewContent = newContent;
        }

        public string TargetPath { get; }
        public string NewContent { get; }
    }

    public static class CharacterTagFileTransaction
    {
        public const string DirectoryPrefix = ".bdtm-character-tag-txn-";
        private const string ManifestName = "manifest.json";

        public static async Task CommitAsync(
            string datasetRoot,
            IEnumerable<CharacterTagFileChange> changes,
            Action<string> beforeReplace = null,
            bool preserveTransactionOnFailure = false,
            CancellationToken cancellationToken = default)
        {
            string root = NormalizeRoot(datasetRoot);
            List<CharacterTagFileChange> changeList = (changes ?? throw new ArgumentNullException(nameof(changes))).ToList();
            if (changeList.Count == 0)
                return;

            string transactionDirectory = Path.Combine(root, DirectoryPrefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(transactionDirectory);
            var manifest = new TransactionManifest();
            try
            {
                for (int index = 0; index < changeList.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    CharacterTagFileChange change = changeList[index];
                    string target = Path.GetFullPath(change.TargetPath);
                    EnsureWithinRoot(root, target);
                    string stagedName = "new-" + index + ".txt";
                    string backupName = "backup-" + index + ".txt";
                    await File.WriteAllTextAsync(Path.Combine(transactionDirectory, stagedName), change.NewContent ?? string.Empty, cancellationToken);
                    bool existed = File.Exists(target);
                    if (existed)
                        File.Copy(target, Path.Combine(transactionDirectory, backupName), true);
                    manifest.Entries.Add(new TransactionEntry
                    {
                        TargetPath = target,
                        Existed = existed,
                        StagedFile = stagedName,
                        BackupFile = existed ? backupName : string.Empty
                    });
                }
                await WriteManifestAsync(transactionDirectory, manifest, cancellationToken);

                foreach (TransactionEntry entry in manifest.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    beforeReplace?.Invoke(entry.TargetPath);
                    string targetDirectory = Path.GetDirectoryName(entry.TargetPath);
                    if (!string.IsNullOrEmpty(targetDirectory))
                        Directory.CreateDirectory(targetDirectory);
                    string siblingTemp = entry.TargetPath + ".bdtm-" + Guid.NewGuid().ToString("N") + ".tmp";
                    File.Copy(Path.Combine(transactionDirectory, entry.StagedFile), siblingTemp, true);
                    File.Move(siblingTemp, entry.TargetPath, true);
                    entry.Applied = true;
                    await WriteManifestAsync(transactionDirectory, manifest, cancellationToken);
                }
                Directory.Delete(transactionDirectory, true);
            }
            catch
            {
                if (!preserveTransactionOnFailure)
                    await RecoverTransactionAsync(transactionDirectory, CancellationToken.None);
                throw;
            }
        }

        public static async Task RecoverIncompleteAsync(string datasetRoot, CancellationToken cancellationToken = default)
        {
            string root = NormalizeRoot(datasetRoot);
            foreach (string directory in Directory.GetDirectories(root, DirectoryPrefix + "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await RecoverTransactionAsync(directory, cancellationToken);
            }
        }

        private static async Task RecoverTransactionAsync(string transactionDirectory, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(transactionDirectory))
                return;
            string manifestPath = Path.Combine(transactionDirectory, ManifestName);
            if (!File.Exists(manifestPath))
            {
                Directory.Delete(transactionDirectory, true);
                return;
            }
            TransactionManifest manifest = JsonConvert.DeserializeObject<TransactionManifest>(
                await File.ReadAllTextAsync(manifestPath, cancellationToken)) ?? new TransactionManifest();
            foreach (TransactionEntry entry in manifest.Entries.AsEnumerable().Reverse())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Existed)
                {
                    string backup = Path.Combine(transactionDirectory, entry.BackupFile);
                    if (File.Exists(backup))
                    {
                        string targetDirectory = Path.GetDirectoryName(entry.TargetPath);
                        if (!string.IsNullOrEmpty(targetDirectory))
                            Directory.CreateDirectory(targetDirectory);
                        File.Copy(backup, entry.TargetPath, true);
                    }
                }
                else if (File.Exists(entry.TargetPath))
                {
                    File.Delete(entry.TargetPath);
                }
            }
            Directory.Delete(transactionDirectory, true);
        }

        private static Task WriteManifestAsync(string transactionDirectory, TransactionManifest manifest, CancellationToken cancellationToken)
        {
            return File.WriteAllTextAsync(
                Path.Combine(transactionDirectory, ManifestName),
                JsonConvert.SerializeObject(manifest, Formatting.Indented),
                cancellationToken);
        }

        private static string NormalizeRoot(string datasetRoot)
        {
            if (string.IsNullOrWhiteSpace(datasetRoot))
                throw new ArgumentException("Dataset root is required.", nameof(datasetRoot));
            string root = Path.GetFullPath(datasetRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException(root);
            return root;
        }

        private static void EnsureWithinRoot(string root, string target)
        {
            string prefix = root + Path.DirectorySeparatorChar;
            if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A transaction target is outside the dataset root: " + target);
        }

        private sealed class TransactionManifest
        {
            public List<TransactionEntry> Entries { get; set; } = new List<TransactionEntry>();
        }

        private sealed class TransactionEntry
        {
            public string TargetPath { get; set; } = string.Empty;
            public bool Existed { get; set; }
            public string StagedFile { get; set; } = string.Empty;
            public string BackupFile { get; set; } = string.Empty;
            public bool Applied { get; set; }
        }
    }
}
