using System.Text;
using BooruDatasetTagManager;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class OnnxInferenceDeviceTests
{
    [Fact]
    public void Fresh_settings_default_to_gpu()
    {
        var settings = new AppSettings();

        Assert.Equal(OnnxInferenceDevice.OnnxDeviceGpu, settings.OnnxTaggerInferenceDevice);
        Assert.False(OnnxInferenceDeviceParser.PreferCpu(settings.OnnxTaggerInferenceDevice));
    }

    [Fact]
    public void LoadData_reads_cpu()
    {
        using var temp = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(temp.Path, "settings.json"),
            "{\"OnnxTaggerInferenceDevice\":\"OnnxDeviceCpu\"}",
            new UTF8Encoding(false));

        var loaded = new AppSettings(temp.Path);

        Assert.Equal(OnnxInferenceDevice.OnnxDeviceCpu, loaded.OnnxTaggerInferenceDevice);
        Assert.True(OnnxInferenceDeviceParser.PreferCpu(loaded.OnnxTaggerInferenceDevice));
    }

    [Fact]
    public void LoadData_rejects_an_unknown_device_value()
    {
        using var temp = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(temp.Path, "settings.json"),
            "{\"OnnxTaggerInferenceDevice\":99}",
            new UTF8Encoding(false));

        var loaded = new AppSettings(temp.Path);

        Assert.Equal(OnnxInferenceDevice.OnnxDeviceGpu, loaded.OnnxTaggerInferenceDevice);
    }

    [Theory]
    [InlineData("cpu", true, OnnxInferenceDevice.OnnxDeviceCpu)]
    [InlineData("GPU", true, OnnxInferenceDevice.OnnxDeviceGpu)]
    [InlineData("OnnxDeviceCpu", true, OnnxInferenceDevice.OnnxDeviceCpu)]
    [InlineData("cuda", false, OnnxInferenceDevice.OnnxDeviceGpu)]
    [InlineData("", false, OnnxInferenceDevice.OnnxDeviceGpu)]
    public void TryParse_accepts_cpu_and_gpu(string text, bool ok, OnnxInferenceDevice expected)
    {
        bool parsed = OnnxInferenceDeviceParser.TryParse(text, out OnnxInferenceDevice device);

        Assert.Equal(ok, parsed);
        Assert.Equal(expected, device);
    }
}
