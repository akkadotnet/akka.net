#if NETCOREAPP

// Adapted from https://github.com/dotnet/core-setup/blob/652b680dff6b1afb9cd26cc3c2e883a664c209fd/src/managed/Microsoft.Extensions.DependencyModel/Resolution/PackageCompilationAssemblyResolver.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Internal.Microsoft.DotNet.PlatformAbstractions;
using Internal.Microsoft.Extensions.DependencyModel;
using Internal.Microsoft.Extensions.DependencyModel.Resolution;
using Xunit.Abstractions;

namespace Xunit
{
    class XunitPackageCompilationAssemblyResolver : ICompilationAssemblyResolver
    {
        readonly IFileSystem fileSystem;
        readonly List<string> nugetPackageDirectories;

        public XunitPackageCompilationAssemblyResolver(IMessageSink internalDiagnosticsMessageSink,
                                                       IFileSystem fileSystem = null)
        {
            nugetPackageDirectories = GetDefaultProbeDirectories(internalDiagnosticsMessageSink);
            this.fileSystem = fileSystem ?? new FileSystemWrapper();
        }

        static List<string> GetDefaultProbeDirectories(IMessageSink internalDiagnosticsMessageSink) =>
            GetDefaultProbeDirectories(RuntimeEnvironment.OperatingSystemPlatform, internalDiagnosticsMessageSink);

        static List<string> GetDefaultProbeDirectories(Platform osPlatform, IMessageSink internalDiagnosticsMessageSink)
        {
            var results = default(List<string>);

            var probeDirectories = AppContext.GetData("PROBING_DIRECTORIES") as string;
            if (!string.IsNullOrEmpty(probeDirectories))
                results = probeDirectories.Split(new char[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries).ToList();
            else
            {
                results = new List<string>();

                // Allow the user to override the default location of NuGet packages
                var packageDirectory = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
                if (!string.IsNullOrEmpty(packageDirectory))
                    results.Add(packageDirectory);
                else
                {
                    string basePath;
                    if (osPlatform == Platform.Windows)
                        basePath = Environment.GetEnvironmentVariable("USERPROFILE");
                    else
                        basePath = Environment.GetEnvironmentVariable("HOME");

                    if (!string.IsNullOrEmpty(basePath))
                        results.Add(Path.Combine(basePath, ".nuget", "packages"));
                }
            }

            if (internalDiagnosticsMessageSink != null)
                internalDiagnosticsMessageSink.OnMessage(new _DiagnosticMessage($"[XunitPackageCompilationAssemblyResolver.GetDefaultProbeDirectories] returns: [{string.Join(",", results.Select(x => $"'{x}'"))}]"));

            return results;
        }

        public bool TryResolveAssemblyPaths(CompilationLibrary library, List<string> assemblies)
        {
            if (nugetPackageDirectories.Count == 0 || !string.Equals(library.Type, "package", StringComparison.OrdinalIgnoreCase))
                return false;

            foreach (var directory in nugetPackageDirectories)
                if (ResolverUtils.TryResolvePackagePath(fileSystem, library, directory, out var packagePath))
                    if (TryResolveFromPackagePath(library, packagePath, out var fullPathsFromPackage))
                    {
                        assemblies.AddRange(fullPathsFromPackage);
                        return true;
                    }

            return false;
        }

        bool TryResolveFromPackagePath(CompilationLibrary library, string basePath, out IEnumerable<string> results)
        {
            var paths = new List<string>();

            foreach (var assembly in library.Assemblies)
            {
                if (!ResolverUtils.TryResolveAssemblyFile(fileSystem, basePath, assembly, out var fullName))
                {
                    // if one of the files can't be found, skip this package path completely.
                    // there are package paths that don't include all of the "ref" assemblies 
                    // (ex. ones created by 'dotnet store')
                    results = null;
                    return false;
                }

                paths.Add(fullName);
            }

            results = paths;
            return true;
        }
    }
}

#endif
