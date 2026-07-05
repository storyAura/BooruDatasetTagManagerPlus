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

    public sealed class AiApiServerAutoTagProvider : IAutoTagProvider
    {
        private readonly Func<AiApiClient> clientFactory;
        private readonly Func<InterragatorSettings> settingsFactory;

        public AiApiServerAutoTagProvider(Func<AiApiClient> clientFactory, Func<InterragatorSettings> settingsFactory)
        {
            this.clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
            this.settingsFactory = settingsFactory ?? throw new ArgumentNullException(nameof(settingsFactory));
        }

        public string Id => "ai-api-server";
        public string DisplayNameKey => "AutoTagProviderAiApiServer";
        public AutoTagProviderCapabilities Capabilities => AutoTagProviderCapabilities.Images
            | AutoTagProviderCapabilities.Video | AutoTagProviderCapabilities.MultipleModels
            | AutoTagProviderCapabilities.DynamicParameters;

        public async Task<AutoTagConnectionResult> ConnectAsync(CancellationToken cancellationToken = default)
        {
            AiApiClient client = clientFactory();
            bool success = client != null && (client.IsConnected || await client.ConnectAsync());
            return new AutoTagConnectionResult(success, success ? string.Empty : "Could not connect to AiApiServer.");
        }

        public Task<IReadOnlyList<AutoTagModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<AutoTagModelDescriptor> models = (clientFactory()?.Config.Interrogators ?? new List<ModelBaseInfo>())
                .Select(model => new AutoTagModelDescriptor
                {
                    Id = model.ModelName,
                    DisplayName = model.ModelName,
                    SupportsVideo = model.SupportedVideo
                }).ToList();
            return Task.FromResult(models);
        }

        public async Task<IReadOnlyList<AutoTagParameterDescriptor>> GetModelParametersAsync(
            string modelId,
            CancellationToken cancellationToken = default)
        {
            ModelParamResponse response = await clientFactory().GetModelParams(modelId);
            return response.Parameters?.Select(parameter => new AutoTagParameterDescriptor
            {
                Key = parameter.Key,
                Type = parameter.Type,
                DefaultValue = parameter.Value,
                Comment = parameter.Comment
            }).ToList() ?? new List<AutoTagParameterDescriptor>();
        }

        public async Task<AutoTagProviderResult> GenerateAsync(
            AutoTagProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            InterragatorSettings settings = settingsFactory();
            List<ModelParameters> models = request.ModelIds.Select(modelId =>
            {
                IEnumerable<AdditionalParameters> configured = settings.InterragatorParams.TryGetValue(modelId, out List<AdditionalParameters> values)
                    ? values
                    : new List<AdditionalParameters>();
                return new ModelParameters
                {
                    ModelName = modelId,
                    AdditionalParameters = configured.Select(parameter => new ModelAdditionalParameters
                    {
                        Key = parameter.Key,
                        Type = parameter.Type,
                        Value = request.Parameters.TryGetValue(parameter.Key, out string overrideValue)
                            ? overrideValue
                            : parameter.Value
                    }).ToList()
                };
            }).ToList();
            AiApiClient.InterrogateResult response = await clientFactory().InterrogateImage(
                request.MediaPath, models, settings.SerializeVramUsage, settings.SkipInternetRequests);
            return new AutoTagProviderResult
            {
                Success = response.Success,
                ErrorMessage = response.Message ?? string.Empty,
                Items = response.GetTagList(settings.UnionMode).Select(item => new AutoTagProviderItem
                {
                    Tag = item.Tag,
                    Confidence = item.Confidence
                }).ToList()
            };
        }
    }
}
