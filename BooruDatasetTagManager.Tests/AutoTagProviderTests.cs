using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class AutoTagProviderTests
{
    [Fact]
    public void RegistryReturnsProvidersByStableId()
    {
        var first = new FakeProvider("openai-compatible");
        var second = new FakeProvider("ai-api-server");
        var registry = new AutoTagProviderRegistry(new IAutoTagProvider[] { first, second });

        Assert.Same(first, registry.GetRequired("openai-compatible"));
        Assert.Equal(new[] { "ai-api-server", "openai-compatible" }, registry.Providers.Select(item => item.Id));
    }

    [Fact]
    public void RegistryRejectsDuplicateIds()
    {
        Assert.Throws<ArgumentException>(() => new AutoTagProviderRegistry(new IAutoTagProvider[]
        {
            new FakeProvider("same"), new FakeProvider("same")
        }));
    }

    private sealed class FakeProvider : IAutoTagProvider
    {
        public FakeProvider(string id) => Id = id;
        public string Id { get; }
        public string DisplayNameKey => "test";
        public AutoTagProviderCapabilities Capabilities => AutoTagProviderCapabilities.Images;
        public Task<AutoTagConnectionResult> ConnectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AutoTagConnectionResult(true, string.Empty));
        public Task<IReadOnlyList<AutoTagModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AutoTagModelDescriptor>>(Array.Empty<AutoTagModelDescriptor>());
        public Task<IReadOnlyList<AutoTagParameterDescriptor>> GetModelParametersAsync(string modelId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AutoTagParameterDescriptor>>(Array.Empty<AutoTagParameterDescriptor>());
        public Task<AutoTagProviderResult> GenerateAsync(AutoTagProviderRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AutoTagProviderResult());
    }
}
