using System;
using System.IO;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class NativeRuntimeLocatorTests
{
    [Fact]
    public void ResolveNativeDirectory_prefers_published_win_x64_folder()
    {
        using var temp = new TemporaryDirectory();
        string ridDir = Path.Combine(temp.Path, NativeRuntimeLocator.RidFolderName);
        Directory.CreateDirectory(ridDir);
        File.WriteAllBytes(Path.Combine(ridDir, NativeRuntimeLocator.OnnxRuntimeFileName), Array.Empty<byte>());
        File.WriteAllBytes(Path.Combine(temp.Path, NativeRuntimeLocator.OnnxRuntimeFileName), Array.Empty<byte>());

        string resolved = NativeRuntimeLocator.ResolveNativeDirectory(temp.Path);

        Assert.Equal(Path.GetFullPath(ridDir), resolved);
    }

    [Fact]
    public void ResolveNativeDirectory_falls_back_to_base_directory()
    {
        using var temp = new TemporaryDirectory();
        File.WriteAllBytes(Path.Combine(temp.Path, NativeRuntimeLocator.OnnxRuntimeFileName), Array.Empty<byte>());

        string resolved = NativeRuntimeLocator.ResolveNativeDirectory(temp.Path);

        Assert.Equal(Path.GetFullPath(temp.Path), resolved);
    }

    [Fact]
    public void ResolveNativeDirectory_finds_nuget_runtimes_layout()
    {
        using var temp = new TemporaryDirectory();
        string native = Path.Combine(temp.Path, "runtimes", NativeRuntimeLocator.RidFolderName, "native");
        Directory.CreateDirectory(native);
        File.WriteAllBytes(Path.Combine(native, NativeRuntimeLocator.OnnxRuntimeFileName), Array.Empty<byte>());

        string resolved = NativeRuntimeLocator.ResolveNativeDirectory(temp.Path);

        Assert.Equal(Path.GetFullPath(native), resolved);
    }

    [Fact]
    public void ResolveNativeDirectory_defaults_to_published_layout_when_missing()
    {
        using var temp = new TemporaryDirectory();

        string resolved = NativeRuntimeLocator.ResolveNativeDirectory(temp.Path);

        Assert.Equal(Path.GetFullPath(Path.Combine(temp.Path, NativeRuntimeLocator.RidFolderName)), resolved);
    }

    [Fact]
    public void Probe_reports_missing_onnxruntime_without_loading_it()
    {
        using var temp = new TemporaryDirectory();

        NativeRuntimeProbe probe = NativeRuntimeLocator.Probe(temp.Path);

        Assert.False(probe.OnnxRuntimeFound);
        Assert.False(probe.DirectMlFound);
        Assert.Contains(
            Path.Combine(temp.Path, NativeRuntimeLocator.RidFolderName),
            probe.SearchedDirectories);
    }

    [Fact]
    public void FormatLoadFailure_includes_inner_exception_and_probe()
    {
        using var temp = new TemporaryDirectory();
        var inner = new DllNotFoundException("Unable to load DLL 'onnxruntime'");
        var outer = new TypeInitializationException("Microsoft.ML.OnnxRuntime.NativeMethods", inner);
        NativeRuntimeProbe probe = NativeRuntimeLocator.Probe(temp.Path);

        string text = NativeRuntimeLocator.FormatLoadFailure(outer, probe);

        Assert.Contains("TypeInitializationException", text);
        Assert.Contains("DllNotFoundException", text);
        Assert.Contains("onnxruntime", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OnnxRuntime=False", text);
    }

    [Fact]
    public void FormatUserMessage_mentions_missing_native_folder()
    {
        using var temp = new TemporaryDirectory();
        var ex = new TypeInitializationException(
            "Microsoft.ML.OnnxRuntime.NativeMethods",
            new DllNotFoundException("Unable to load DLL 'onnxruntime'"));

        string text = NativeRuntimeLocator.FormatUserMessage(ex, temp.Path);

        Assert.Contains("TaggerOnnxNativeMissing", text);
        Assert.Contains("DllNotFoundException", text);
    }

    [Fact]
    public void LooksLikeCompatibilityShim_is_false_when_both_reports_are_windows_10()
    {
        Assert.False(NativeRuntimeLocator.LooksLikeCompatibilityShim(
            new Version(10, 0, 19045),
            new Version(10, 0, 19045)));
    }

    [Fact]
    public void LooksLikeCompatibilityShim_is_true_when_either_side_is_vista()
    {
        Assert.True(NativeRuntimeLocator.LooksLikeCompatibilityShim(
            new Version(10, 0, 19045),
            new Version(6, 0, 6000)));
        Assert.True(NativeRuntimeLocator.LooksLikeCompatibilityShim(
            new Version(6, 0, 6000),
            new Version(10, 0, 19045)));
    }

    [Fact]
    public void IsNativeLoadFailure_detects_typinit_and_dll_not_found()
    {
        Assert.True(NativeRuntimeLocator.IsNativeLoadFailure(
            new TypeInitializationException("Microsoft.ML.OnnxRuntime.NativeMethods", new DllNotFoundException())));
        Assert.True(NativeRuntimeLocator.IsNativeLoadFailure(new DllNotFoundException("onnxruntime")));
        Assert.False(NativeRuntimeLocator.IsNativeLoadFailure(new InvalidOperationException("session is not loaded")));
    }

    [Fact]
    public void ConfigureSearchPath_does_not_throw_when_native_folder_is_missing()
    {
        using var temp = new TemporaryDirectory();

        NativeRuntimeConfigureResult result = NativeRuntimeLocator.ConfigureSearchPath(temp.Path);

        Assert.False(result.OnnxRuntimeLoaded);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(temp.Path, NativeRuntimeLocator.RidFolderName)),
            result.NativeDirectory);
    }

    [Fact]
    public void Program_configures_native_search_path_before_cli()
    {
        string source = File.ReadAllText(Path.Combine(ProjectDirectory(), "Program.cs"));
        int configure = source.IndexOf("NativeRuntimeLocator.ConfigureSearchPath", StringComparison.Ordinal);
        int cli = source.IndexOf("CliCommands.IsCliInvocation", StringComparison.Ordinal);

        Assert.True(configure >= 0, "ConfigureSearchPath missing from Program.cs");
        Assert.True(cli >= 0, "CLI branch missing from Program.cs");
        Assert.True(configure < cli, "ConfigureSearchPath must run before the CLI return.");
    }

    [Fact]
    public void FormOnnxTagger_surfaces_native_load_failures()
    {
        string source = File.ReadAllText(Path.Combine(ProjectDirectory(), "Form_OnnxTagger.cs"));
        Assert.Contains("NativeRuntimeLocator.IsNativeLoadFailure", source);
        Assert.Contains("NativeRuntimeLocator.FormatUserMessage", source);
        Assert.Contains("OnnxBatchRunner.Run", source);
    }

    [Fact]
    public void Project_ships_app_manifest_and_copies_vc_crt_sidecar()
    {
        string project = ProjectDirectory();
        string csproj = File.ReadAllText(Path.Combine(project, "BooruDatasetTagManager.csproj"));
        Assert.Contains("<ApplicationManifest>app.manifest</ApplicationManifest>", csproj);
        Assert.Contains("CopyVcRuntimeSidecar", csproj);
        Assert.Contains("msvcp140.dll", csproj);
        Assert.True(File.Exists(Path.Combine(project, "app.manifest")));
        Assert.Contains("supportedOS", File.ReadAllText(Path.Combine(project, "app.manifest")));
    }

    private static string ProjectDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "BooruDatasetTagManager", "Program.cs");
            if (File.Exists(candidate))
                return Path.Combine(dir.FullName, "BooruDatasetTagManager");

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not find main project directory.");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"BDTM-native-tests-{Guid.NewGuid():N}");

        public TemporaryDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            Directory.Delete(Path, true);
        }
    }
}
