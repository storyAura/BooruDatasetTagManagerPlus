using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Finds and preloads ONNX Runtime native binaries after a single-file
    /// publish moves them into win-x64/. Program-free so tests can link it.
    /// </summary>
    public static class NativeRuntimeLocator
    {
        public const string OnnxRuntimeFileName = "onnxruntime.dll";
        public const string DirectMlFileName = "DirectML.dll";

        private const uint LoadLibrarySearchDefaultDirs = 0x00001000;
        private const uint LoadLibrarySearchUserDirs = 0x00000400;

        public static string RidFolderName =>
            Environment.Is64BitProcess ? "win-x64" : "win-x86";

        public static string ResolveNativeDirectory(string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
                return null;

            string rid = RidFolderName;
            foreach (string candidate in EnumerateSearchDirectories(baseDirectory, rid))
            {
                if (File.Exists(Path.Combine(candidate, OnnxRuntimeFileName)))
                    return Path.GetFullPath(candidate);
            }

            return Path.GetFullPath(Path.Combine(baseDirectory, rid));
        }

        public static IReadOnlyList<string> EnumerateSearchDirectories(string baseDirectory)
        {
            return EnumerateSearchDirectories(baseDirectory, RidFolderName);
        }

        private static string[] EnumerateSearchDirectories(string baseDirectory, string rid)
        {
            return new[]
            {
                Path.Combine(baseDirectory, rid),
                baseDirectory,
                Path.Combine(baseDirectory, "runtimes", rid, "native")
            };
        }

        public static NativeRuntimeProbe Probe(string baseDirectory = null)
        {
            baseDirectory ??= AppContext.BaseDirectory;
            string nativeDirectory = ResolveNativeDirectory(baseDirectory);
            bool onnxFound = !string.IsNullOrEmpty(nativeDirectory)
                && File.Exists(Path.Combine(nativeDirectory, OnnxRuntimeFileName));
            bool directMlFound = !string.IsNullOrEmpty(nativeDirectory)
                && File.Exists(Path.Combine(nativeDirectory, DirectMlFileName));
            bool vcFound = HasVcRuntime(nativeDirectory);

            return new NativeRuntimeProbe
            {
                BaseDirectory = baseDirectory,
                NativeDirectory = nativeDirectory,
                OnnxRuntimePath = onnxFound ? Path.Combine(nativeDirectory, OnnxRuntimeFileName) : null,
                OnnxRuntimeFound = onnxFound,
                DirectMlFound = directMlFound,
                VcRuntimeFound = vcFound,
                SearchedDirectories = EnumerateSearchDirectories(baseDirectory)
            };
        }

        public static NativeRuntimeConfigureResult ConfigureSearchPath(string baseDirectory = null)
        {
            var result = new NativeRuntimeConfigureResult();
            try
            {
                NativeRuntimeProbe probe = Probe(baseDirectory);
                result.NativeDirectory = probe.NativeDirectory;
                if (string.IsNullOrEmpty(probe.NativeDirectory) || !Directory.Exists(probe.NativeDirectory))
                    return result;

                try
                {
                    result.SetDllDirectoryOk = SetDllDirectory(probe.NativeDirectory);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine("NativeRuntimeLocator.SetDllDirectory: " + ex.Message);
                }

                TryEnableUserDllDirectories(probe.NativeDirectory, result);

                TryLoadAbsolute(Path.Combine(probe.NativeDirectory, "msvcp140.dll"));
                TryLoadAbsolute(Path.Combine(probe.NativeDirectory, "msvcp140_1.dll"));
                TryLoadAbsolute(Path.Combine(probe.NativeDirectory, "vcruntime140.dll"));
                TryLoadAbsolute(Path.Combine(probe.NativeDirectory, "vcruntime140_1.dll"));
                result.OnnxRuntimeLoaded = TryLoadAbsolute(Path.Combine(probe.NativeDirectory, OnnxRuntimeFileName));
                result.DirectMlLoaded = TryLoadAbsolute(Path.Combine(probe.NativeDirectory, DirectMlFileName));
                Trace.WriteLine(
                    "NativeRuntimeLocator dir=" + probe.NativeDirectory
                    + " onnxFound=" + probe.OnnxRuntimeFound
                    + " onnxLoaded=" + result.OnnxRuntimeLoaded
                    + " setDll=" + result.SetDllDirectoryOk);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("NativeRuntimeLocator.ConfigureSearchPath: " + ex);
            }

            return result;
        }

        public static bool LooksLikeCompatibilityShim()
        {
            TryGetRtlVersion(out Version rtl);
            TryGetVersionEx(out Version getVersionEx);
            return LooksLikeCompatibilityShim(rtl, getVersionEx);
        }

        public static bool LooksLikeCompatibilityShim(Version rtlVersion, Version getVersionEx)
        {
            if (rtlVersion != null && rtlVersion.Major < 10)
                return true;
            if (getVersionEx != null && getVersionEx.Major < 10)
                return true;
            return false;
        }

        public static bool IsNativeLoadFailure(Exception ex)
        {
            for (Exception cursor = ex; cursor != null; cursor = cursor.InnerException)
            {
                if (cursor is DllNotFoundException or TypeInitializationException)
                    return true;
                if (cursor.GetType().FullName != null
                    && cursor.GetType().FullName.IndexOf("NativeMethods", StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        public static string FormatExceptionChain(Exception ex)
        {
            if (ex == null)
                return string.Empty;

            var builder = new StringBuilder();
            for (Exception cursor = ex; cursor != null; cursor = cursor.InnerException)
            {
                if (builder.Length > 0)
                    builder.AppendLine();
                builder.Append(cursor.GetType().Name).Append(": ").Append(cursor.Message);
            }

            return builder.ToString();
        }

        public static string FormatLoadFailure(Exception ex, NativeRuntimeProbe probe = null)
        {
            var builder = new StringBuilder();
            builder.Append(FormatExceptionChain(ex));
            if (probe != null)
            {
                if (builder.Length > 0)
                    builder.AppendLine();
                builder.Append("NativeDirectory=").Append(probe.NativeDirectory);
                builder.Append(" OnnxRuntime=").Append(probe.OnnxRuntimeFound);
                builder.Append(" DirectML=").Append(probe.DirectMlFound);
                builder.Append(" VcRuntime=").Append(probe.VcRuntimeFound);
            }

            return builder.ToString();
        }

        public static string FormatUserMessage(Exception ex, string baseDirectory = null)
        {
            NativeRuntimeProbe probe = Probe(baseDirectory);
            var builder = new StringBuilder();
            if (!probe.OnnxRuntimeFound)
                AppendHint(builder, I18n.GetText("TaggerOnnxNativeMissing"));
            if (!probe.VcRuntimeFound)
                AppendHint(builder, I18n.GetText("TaggerOnnxVcRedistMissing"));
            if (LooksLikeCompatibilityShim())
                AppendHint(builder, I18n.GetText("TaggerOnnxCompatModeHint"));
            if (builder.Length > 0)
                builder.AppendLine();
            builder.Append(FormatLoadFailure(ex, probe));
            return builder.ToString().Trim();
        }

        public static string FormatStartupLogLine()
        {
            TryGetRtlVersion(out Version rtl);
            TryGetVersionEx(out Version getVersionEx);
            return "OS: " + Environment.OSVersion
                + ", OSDescription: " + RuntimeInformation.OSDescription
                + ", RtlGetVersion: " + (rtl != null ? rtl.ToString() : "?")
                + ", GetVersionEx: " + (getVersionEx != null ? getVersionEx.ToString() : "?")
                + ", 64-bit: " + Environment.Is64BitProcess
                + ", compatShim: " + LooksLikeCompatibilityShim(rtl, getVersionEx);
        }

        private static void AppendHint(StringBuilder builder, string hint)
        {
            if (string.IsNullOrWhiteSpace(hint))
                return;
            if (builder.Length > 0)
                builder.AppendLine();
            builder.Append(hint);
        }

        private static bool HasVcRuntime(string nativeDirectory)
        {
            if (!string.IsNullOrEmpty(nativeDirectory)
                && File.Exists(Path.Combine(nativeDirectory, "msvcp140.dll")))
            {
                return true;
            }

            try
            {
                return File.Exists(Path.Combine(Environment.SystemDirectory, "msvcp140.dll"));
            }
            catch
            {
                return false;
            }
        }

        private static bool TryLoadAbsolute(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
                return false;

            try
            {
                return NativeLibrary.TryLoad(fullPath, out _);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("NativeRuntimeLocator.TryLoad " + fullPath + ": " + ex.Message);
                return false;
            }
        }

        private static void TryEnableUserDllDirectories(string nativeDirectory, NativeRuntimeConfigureResult result)
        {
            try
            {
                result.SetDefaultDllDirectoriesOk = SetDefaultDllDirectories(
                    LoadLibrarySearchDefaultDirs | LoadLibrarySearchUserDirs);
                result.AddDllDirectoryCookie = AddDllDirectory(nativeDirectory);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("NativeRuntimeLocator.AddDllDirectory: " + ex.Message);
            }
        }

        internal static bool TryGetRtlVersion(out Version version)
        {
            version = null;
            try
            {
                var info = new OsVersionInfoEx
                {
                    dwOSVersionInfoSize = Marshal.SizeOf<OsVersionInfoEx>()
                };
                if (RtlGetVersion(ref info) != 0)
                    return false;
                version = new Version(info.dwMajorVersion, info.dwMinorVersion, info.dwBuildNumber);
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryGetVersionEx(out Version version)
        {
            version = null;
            try
            {
                var info = new OsVersionInfoEx
                {
                    dwOSVersionInfoSize = Marshal.SizeOf<OsVersionInfoEx>()
                };
                if (!GetVersionEx(ref info))
                    return false;
                version = new Version(info.dwMajorVersion, info.dwMinorVersion, info.dwBuildNumber);
                return true;
            }
            catch
            {
                return false;
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(uint directoryFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr AddDllDirectory(string newDirectory);

        [DllImport("ntdll.dll")]
        private static extern int RtlGetVersion(ref OsVersionInfoEx versionInfo);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetVersionEx(ref OsVersionInfoEx osvi);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OsVersionInfoEx
        {
            public int dwOSVersionInfoSize;
            public int dwMajorVersion;
            public int dwMinorVersion;
            public int dwBuildNumber;
            public int dwPlatformId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szCSDVersion;
        }
    }

    public sealed class NativeRuntimeProbe
    {
        public string BaseDirectory { get; init; }
        public string NativeDirectory { get; init; }
        public string OnnxRuntimePath { get; init; }
        public bool OnnxRuntimeFound { get; init; }
        public bool DirectMlFound { get; init; }
        public bool VcRuntimeFound { get; init; }
        public IReadOnlyList<string> SearchedDirectories { get; init; } = Array.Empty<string>();
    }

    public sealed class NativeRuntimeConfigureResult
    {
        public string NativeDirectory { get; set; }
        public bool SetDllDirectoryOk { get; set; }
        public bool SetDefaultDllDirectoriesOk { get; set; }
        public IntPtr AddDllDirectoryCookie { get; set; }
        public bool OnnxRuntimeLoaded { get; set; }
        public bool DirectMlLoaded { get; set; }
    }
}
