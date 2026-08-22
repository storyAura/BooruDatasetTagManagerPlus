using System;
using System.IO;
using Newtonsoft.Json;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class OnnxModelIntegrityTests
{
    [Fact]
    public void ShouldClearCachedModel_keeps_file_on_environment_errors()
    {
        Assert.False(OnnxModelIntegrity.ShouldClearCachedModel(new DllNotFoundException("onnxruntime")));
        Assert.False(OnnxModelIntegrity.ShouldClearCachedModel(new EntryPointNotFoundException("OrtGetApi")));
        Assert.False(OnnxModelIntegrity.ShouldClearCachedModel(new BadImageFormatException("native")));
        Assert.False(OnnxModelIntegrity.ShouldClearCachedModel(
            new TypeInitializationException("Microsoft.ML.OnnxRuntime.NativeMethods", new DllNotFoundException())));
        Assert.False(OnnxModelIntegrity.ShouldClearCachedModel(new FileNotFoundException("missing")));
        Assert.False(OnnxModelIntegrity.ShouldClearCachedModel(new IOException("The process cannot access the file")));
        Assert.False(OnnxModelIntegrity.ShouldClearCachedModel(new UnauthorizedAccessException()));
        Assert.False(OnnxModelIntegrity.ShouldClearCachedModel(new OutOfMemoryException()));
        Assert.False(OnnxModelIntegrity.ShouldClearCachedModel(new NotSupportedException("float16")));
        Assert.False(OnnxModelIntegrity.ShouldClearCachedModel(
            new InvalidOperationException("DirectML execution provider failed to create session")));
        Assert.False(OnnxModelIntegrity.ShouldClearCachedModel(
            new InvalidOperationException("Load model from model.onnx failed:Cannot open file because it is being used by another process")));
    }

    [Fact]
    public void ShouldClearCachedModel_purges_on_parse_failures()
    {
        Assert.True(OnnxModelIntegrity.ShouldClearCachedModel(
            new InvalidOperationException("Load model from C:\\m.onnx failed:Protobuf parsing failed.")));
        Assert.True(OnnxModelIntegrity.ShouldClearCachedModel(
            new JsonReaderException("Unexpected end when reading JSON")));
        Assert.True(OnnxModelIntegrity.ShouldClearCachedModel(new InvalidDataException("truncated")));
    }

    [Fact]
    public void ShouldClearCachedModel_keeps_file_on_unknown_exceptions()
    {
        Assert.False(OnnxModelIntegrity.ShouldClearCachedModel(new InvalidOperationException("session is not loaded")));
        Assert.False(OnnxModelIntegrity.ShouldClearCachedModel(new Exception("generic failure")));
    }

    [Fact]
    public void IsTransientFileLock_detects_sharing_violations()
    {
        Assert.True(OnnxModelIntegrity.IsTransientFileLock(new IOException("The process cannot access the file")));
        Assert.True(OnnxModelIntegrity.IsTransientFileLock(
            new InvalidOperationException("cannot access the file because it is being used by another process")));
        Assert.False(OnnxModelIntegrity.IsTransientFileLock(
            new InvalidOperationException("Protobuf parsing failed")));
    }

    [Fact]
    public void RunWithTransientLockRetry_succeeds_after_lock_clears()
    {
        int attempts = 0;
        OnnxModelIntegrity.RunWithTransientLockRetry(() =>
        {
            attempts++;
            if (attempts < 3)
                throw new IOException("The process cannot access the file");
        }, attempts: 5, delayMilliseconds: 1);

        Assert.Equal(3, attempts);
    }
}
