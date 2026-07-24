using BooruDatasetTagManager.AiApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BooruDatasetTagManager
{
    public sealed class OpenAiCompatibleAutoTagProvider : IAutoTagProvider
    {
        private readonly Func<AiOpenAiClient> clientFactory;

        public OpenAiCompatibleAutoTagProvider(Func<AiOpenAiClient> clientFactory)
        {
            this.clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        }

        public string Id => "openai-compatible";
        public string DisplayNameKey => "AutoTagProviderOpenAiCompatible";
        public AutoTagProviderCapabilities Capabilities => AutoTagProviderCapabilities.Images
            | AutoTagProviderCapabilities.Video | AutoTagProviderCapabilities.MultipleModels;

        public async Task<AutoTagConnectionResult> ConnectAsync(CancellationToken cancellationToken = default)
        {
            AiOpenAiClient client = clientFactory();
            if (client == null)
                return new AutoTagConnectionResult(false, "OpenAI-compatible endpoint is not configured.");
            if (client.IsConnected)
                return new AutoTagConnectionResult(true, string.Empty);
            var response = await client.ConnectAsync(cancellationToken);
            return new AutoTagConnectionResult(response.Result, response.ErrMessage);
        }

        public Task<IReadOnlyList<AutoTagModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<AutoTagModelDescriptor> models = (clientFactory()?.Models ?? new List<string>())
                .Select(model => new AutoTagModelDescriptor { Id = model, DisplayName = model, SupportsVideo = true })
                .ToList();
            return Task.FromResult(models);
        }

        public Task<IReadOnlyList<AutoTagParameterDescriptor>> GetModelParametersAsync(
            string modelId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<AutoTagParameterDescriptor>>(Array.Empty<AutoTagParameterDescriptor>());
        }

        public async Task<AutoTagProviderResult> GenerateAsync(
            AutoTagProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            AiOpenAiClient client = clientFactory();
            if (client == null)
                return new AutoTagProviderResult { Success = false, ErrorMessage = "OpenAI-compatible endpoint is not configured." };
            var response = await client.GetTagsWithAutoTagger(request.MediaPath, true, cancellationToken);
            return new AutoTagProviderResult
            {
                Success = response.data != null && string.IsNullOrEmpty(response.errorMessage),
                Canceled = response.canceled,
                ErrorMessage = response.errorMessage ?? string.Empty,
                Items = response.data?.Select(item => new AutoTagProviderItem
                {
                    Tag = item.Tag,
                    Confidence = item.Confidence
                }).ToList() ?? new List<AutoTagProviderItem>()
            };
        }
    }

    // The legacy AiApiServerAutoTagProvider ("ai-api-server", backed by the
    // external Python AiApiServer) was removed together with the server:
    // old configs selecting it are migrated to "openai-compatible" at startup.
}
