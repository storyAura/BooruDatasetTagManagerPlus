using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BooruDatasetTagManager
{
    [Flags]
    public enum AutoTagProviderCapabilities
    {
        None = 0,
        Images = 1,
        Video = 2,
        MultipleModels = 4,
        DynamicParameters = 8
    }

    public sealed class AutoTagConnectionResult
    {
        public AutoTagConnectionResult(bool success, string errorMessage)
        {
            Success = success;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public bool Success { get; }
        public string ErrorMessage { get; }
    }

    public sealed class AutoTagModelDescriptor
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool SupportsVideo { get; set; }
    }

    public sealed class AutoTagParameterDescriptor
    {
        public string Key { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string DefaultValue { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
    }

    public sealed class AutoTagProviderRequest
    {
        public string MediaPath { get; set; } = string.Empty;
        public IReadOnlyList<string> ModelIds { get; set; } = Array.Empty<string>();
        public IReadOnlyDictionary<string, string> Parameters { get; set; } =
            new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public sealed class AutoTagProviderItem
    {
        public string Tag { get; set; } = string.Empty;
        public float Confidence { get; set; }
    }

    public sealed class AutoTagProviderResult
    {
        public bool Success { get; set; } = true;
        public bool Canceled { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public IReadOnlyList<AutoTagProviderItem> Items { get; set; } = Array.Empty<AutoTagProviderItem>();
    }

    public interface IAutoTagProvider
    {
        string Id { get; }
        string DisplayNameKey { get; }
        AutoTagProviderCapabilities Capabilities { get; }
        Task<AutoTagConnectionResult> ConnectAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<AutoTagModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<AutoTagParameterDescriptor>> GetModelParametersAsync(
            string modelId,
            CancellationToken cancellationToken = default);
        Task<AutoTagProviderResult> GenerateAsync(
            AutoTagProviderRequest request,
            CancellationToken cancellationToken = default);
    }

    public sealed class AutoTagProviderRegistry
    {
        private readonly Dictionary<string, IAutoTagProvider> providers;

        public AutoTagProviderRegistry(IEnumerable<IAutoTagProvider> providers)
        {
            if (providers == null)
                throw new ArgumentNullException(nameof(providers));
            this.providers = new Dictionary<string, IAutoTagProvider>(StringComparer.OrdinalIgnoreCase);
            foreach (IAutoTagProvider provider in providers)
            {
                if (provider == null || string.IsNullOrWhiteSpace(provider.Id))
                    throw new ArgumentException("Every auto-tag provider must have a stable ID.", nameof(providers));
                if (!this.providers.TryAdd(provider.Id, provider))
                    throw new ArgumentException("Duplicate auto-tag provider ID: " + provider.Id, nameof(providers));
            }
        }

        public IReadOnlyList<IAutoTagProvider> Providers => providers.Values.OrderBy(item => item.Id, StringComparer.Ordinal).ToList();

        public IAutoTagProvider GetRequired(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !providers.TryGetValue(id, out IAutoTagProvider provider))
                throw new KeyNotFoundException("Auto-tag provider was not registered: " + id);
            return provider;
        }
    }
}
