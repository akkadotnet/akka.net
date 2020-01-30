// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if NETCOREAPP

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Internal.Microsoft.Extensions.DependencyModel
{
    internal class DependencyContextJsonReader : IDependencyContextReader
    {
        static readonly string[] EmptyStringArray = new string[0];

        private readonly IDictionary<string, string> _stringPool = new Dictionary<string, string>();

        public DependencyContext Read(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            using (var streamReader = new StreamReader(stream))
            {
                return Read(streamReader);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _stringPool.Clear();
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        private DependencyContext Read(TextReader reader)
        {
            // {
            //     "runtimeTarget": {...},
            //     "compilationOptions": {...},
            //     "targets": {...},
            //     "libraries": {...},
            //     "runtimes": {...}
            // }

            var root = JsonDeserializer.Deserialize(reader) as JsonObject;
            if (root == null)
                return null;

            var runtime = string.Empty;
            var framework = string.Empty;
            var isPortable = true;

            ReadRuntimeTarget(root.ValueAsJsonObject(DependencyContextStrings.RuntimeTargetPropertyName), out var runtimeTargetName, out var runtimeSignature);
            var compilationOptions = ReadCompilationOptions(root.ValueAsJsonObject(DependencyContextStrings.CompilationOptionsPropertName));
            var targets = ReadTargets(root.ValueAsJsonObject(DependencyContextStrings.TargetsPropertyName));
            var libraryStubs = ReadLibraries(root.ValueAsJsonObject(DependencyContextStrings.LibrariesPropertyName));
            var runtimeFallbacks = ReadRuntimes(root.ValueAsJsonObject(DependencyContextStrings.RuntimesPropertyName));

            if (compilationOptions == null)
                compilationOptions = CompilationOptions.Default;

            Target runtimeTarget = SelectRuntimeTarget(targets, runtimeTargetName);
            runtimeTargetName = runtimeTarget?.Name;

            if (runtimeTargetName != null)
            {
                var seperatorIndex = runtimeTargetName.IndexOf(DependencyContextStrings.VersionSeperator);
                if (seperatorIndex > -1 && seperatorIndex < runtimeTargetName.Length)
                {
                    runtime = runtimeTargetName.Substring(seperatorIndex + 1);
                    framework = runtimeTargetName.Substring(0, seperatorIndex);
                    isPortable = false;
                }
                else
                {
                    framework = runtimeTargetName;
                }
            }

            Target compileTarget = null;

            var ridlessTarget = targets.FirstOrDefault(t => !IsRuntimeTarget(t.Name));
            if (ridlessTarget != null)
            {
                compileTarget = ridlessTarget;
                if (runtimeTarget == null)
                {
                    runtimeTarget = compileTarget;
                    framework = ridlessTarget.Name;
                }
            }

            if (runtimeTarget == null)
                throw new FormatException("No runtime target found");

            return new DependencyContext(
                new TargetInfo(framework, runtime, runtimeSignature, isPortable),
                compilationOptions,
                CreateLibraries(compileTarget?.Libraries, false, libraryStubs).Cast<CompilationLibrary>().ToArray(),
                CreateLibraries(runtimeTarget.Libraries, true, libraryStubs).Cast<RuntimeLibrary>().ToArray(),
                runtimeFallbacks ?? Enumerable.Empty<RuntimeFallbacks>());
        }

        private Target SelectRuntimeTarget(List<Target> targets, string runtimeTargetName)
        {
            Target target;

            if (targets == null || targets.Count == 0)
                throw new FormatException("Dependency file does not have 'targets' section");

            if (!string.IsNullOrEmpty(runtimeTargetName))
            {
                target = targets.FirstOrDefault(t => t.Name == runtimeTargetName);
                if (target == null)
                    throw new FormatException($"Target with name {runtimeTargetName} not found");
            }
            else
            {
                target = targets.FirstOrDefault(t => IsRuntimeTarget(t.Name));
            }

            return target;
        }

        private bool IsRuntimeTarget(string name)
            => name.Contains(DependencyContextStrings.VersionSeperator);

        private void ReadRuntimeTarget(JsonObject runtimeTargetJson, out string runtimeTargetName, out string runtimeSignature)
        {
            // {
            //     "name": ".NETCoreApp,Version=v1.0",
            //     "signature": "35bd60f1a92c048eea72ff8160ba07b616ebd0f6"
            // }

            runtimeTargetName = runtimeTargetJson?.ValueAsString(DependencyContextStrings.RuntimeTargetNamePropertyName);
            runtimeSignature = runtimeTargetJson?.ValueAsString(DependencyContextStrings.RuntimeTargetSignaturePropertyName);
        }

        private CompilationOptions ReadCompilationOptions(JsonObject compilationOptionsJson)
        {
            // {
            //      "defines": ["","",...],
            //      "languageVersion": "...",
            //      "platform": "...",
            //      "allowUnsafe: "true|false",
            //      "warningsAsErrors": "true|false",
            //      "optimize": "true|false",
            //      "keyFile": "...",
            //      "delaySign": "true|false",
            //      "publicSign": "true|false",
            //      "debugType": "...",
            //      "emitEntryPoint: "true|false",
            //      "xmlDoc": "true|false"
            // }

            if (compilationOptionsJson == null)
                return null;

            var defines = compilationOptionsJson.ValueAsStringArray(DependencyContextStrings.DefinesPropertyName) ?? EmptyStringArray;
            var languageVersion = compilationOptionsJson.ValueAsString(DependencyContextStrings.LanguageVersionPropertyName);
            var platform = compilationOptionsJson.ValueAsString(DependencyContextStrings.PlatformPropertyName);
            var allowUnsafe = compilationOptionsJson.ValueAsNullableBoolean(DependencyContextStrings.AllowUnsafePropertyName);
            var warningsAsErrors = compilationOptionsJson.ValueAsNullableBoolean(DependencyContextStrings.WarningsAsErrorsPropertyName);
            var optimize = compilationOptionsJson.ValueAsNullableBoolean(DependencyContextStrings.OptimizePropertyName);
            var keyFile = compilationOptionsJson.ValueAsString(DependencyContextStrings.KeyFilePropertyName);
            var delaySign = compilationOptionsJson.ValueAsNullableBoolean(DependencyContextStrings.DelaySignPropertyName);
            var publicSign = compilationOptionsJson.ValueAsNullableBoolean(DependencyContextStrings.PublicSignPropertyName);
            var debugType = compilationOptionsJson.ValueAsString(DependencyContextStrings.DebugTypePropertyName);
            var emitEntryPoint = compilationOptionsJson.ValueAsNullableBoolean(DependencyContextStrings.EmitEntryPointPropertyName);
            var generateXmlDocumentation = compilationOptionsJson.ValueAsNullableBoolean(DependencyContextStrings.GenerateXmlDocumentationPropertyName);

            return new CompilationOptions(defines, languageVersion, platform, allowUnsafe, warningsAsErrors, optimize, keyFile, delaySign, publicSign, debugType, emitEntryPoint, generateXmlDocumentation);
        }

        private List<Target> ReadTargets(JsonObject targetsJson)
        {
            // Object dictionary: string => object

            var targets = new List<Target>();

            if (targetsJson != null)
                foreach (var key in targetsJson.Keys)
                    targets.Add(ReadTarget(key, targetsJson.ValueAsJsonObject(key)));

            return targets;
        }

        private Target ReadTarget(string targetName, JsonObject targetJson)
        {
            // Object dictionary: string => object

            var libraries = new List<TargetLibrary>();

            foreach (var key in targetJson.Keys)
                libraries.Add(ReadTargetLibrary(key, targetJson.ValueAsJsonObject(key)));

            return new Target { Name = targetName, Libraries = libraries };
        }

        private TargetLibrary ReadTargetLibrary(string targetLibraryName, JsonObject targetLibraryJson)
        {
            // {
            //     "dependencies": {...},
            //     "runtime": {...},       # Dictionary: name => {}
            //     "native": {...},        # Dictionary: name => {}
            //     "compile": {...},       # Dictionary: name => {}
            //     "runtime": {...},
            //     "resources": {...},
            //     "compileOnly": "true|false"
            // }

            var dependencies = ReadTargetLibraryDependencies(targetLibraryJson.ValueAsJsonObject(DependencyContextStrings.DependenciesPropertyName));
            var runtimes = targetLibraryJson.ValueAsJsonObject(DependencyContextStrings.RuntimeAssembliesKey)?.Keys;
            var natives = targetLibraryJson.ValueAsJsonObject(DependencyContextStrings.NativeLibrariesKey)?.Keys;
            var compilations = targetLibraryJson.ValueAsJsonObject(DependencyContextStrings.CompileTimeAssembliesKey)?.Keys;
            var runtimeTargets = ReadTargetLibraryRuntimeTargets(targetLibraryJson.ValueAsJsonObject(DependencyContextStrings.RuntimeTargetsPropertyName));
            var resources = ReadTargetLibraryResources(targetLibraryJson.ValueAsJsonObject(DependencyContextStrings.ResourceAssembliesPropertyName));
            var compileOnly = targetLibraryJson.ValueAsNullableBoolean(DependencyContextStrings.CompilationOnlyPropertyName);

            return new TargetLibrary
            {
                Name = targetLibraryName,
                Dependencies = dependencies ?? Enumerable.Empty<Dependency>(),
                Runtimes = runtimes?.ToList(),
                Natives = natives?.ToList(),
                Compilations = compilations?.ToList(),
                RuntimeTargets = runtimeTargets,
                Resources = resources,
                CompileOnly = compileOnly
            };
        }

        public IEnumerable<Dependency> ReadTargetLibraryDependencies(JsonObject targetLibraryDependenciesJson)
        {
            // Object dictionary: string => string

            var dependencies = new List<Dependency>();

            if (targetLibraryDependenciesJson != null)
                foreach (var key in targetLibraryDependenciesJson.Keys)
                    dependencies.Add(new Dependency(Pool(key), Pool(targetLibraryDependenciesJson.ValueAsString(key))));

            return dependencies;
        }

        private List<RuntimeTargetEntryStub> ReadTargetLibraryRuntimeTargets(JsonObject targetLibraryRuntimeTargetsJson)
        {
            // Object dictionary: string => { "rid": "...", "assetType": "..." }

            var runtimeTargets = new List<RuntimeTargetEntryStub>();

            if (targetLibraryRuntimeTargetsJson != null)
            {
                foreach (var key in targetLibraryRuntimeTargetsJson.Keys)
                {
                    var runtimeTargetJson = targetLibraryRuntimeTargetsJson.ValueAsJsonObject(key);

                    runtimeTargets.Add(new RuntimeTargetEntryStub
                    {
                        Path = key,
                        Rid = Pool(runtimeTargetJson?.ValueAsString(DependencyContextStrings.RidPropertyName)),
                        Type = Pool(runtimeTargetJson?.ValueAsString(DependencyContextStrings.AssetTypePropertyName))
                    });
                }
            }

            return runtimeTargets;
        }

        private List<ResourceAssembly> ReadTargetLibraryResources(JsonObject targetLibraryResourcesJson)
        {
            // Object dictionary: string => { "locale": "..." }

            var resources = new List<ResourceAssembly>();

            if (targetLibraryResourcesJson != null)
            {
                foreach (var key in targetLibraryResourcesJson.Keys)
                {
                    string locale = targetLibraryResourcesJson.ValueAsJsonObject(key)?.ValueAsString(DependencyContextStrings.LocalePropertyName);

                    if (locale != null)
                        resources.Add(new ResourceAssembly(key, Pool(locale)));
                }
            }

            return resources;
        }

        private Dictionary<string, LibraryStub> ReadLibraries(JsonObject librariesJson)
        {
            // Object dictionary: string => object

            var libraries = new Dictionary<string, LibraryStub>();

            if (librariesJson != null)
                foreach (var key in librariesJson.Keys)
                    libraries.Add(Pool(key), ReadLibrary(librariesJson.ValueAsJsonObject(key)));

            return libraries;
        }

        private LibraryStub ReadLibrary(JsonObject libraryJson)
        {
            // {
            //     "sha512": "...",
            //     "type": "...",
            //     "serviceable: "true|false",
            //     "path": "...",
            //     "hashPath: "...",
            //     "runtimeStoreManifestName": "..."
            // }

            string hash = libraryJson.ValueAsString(DependencyContextStrings.Sha512PropertyName);
            string type = libraryJson.ValueAsString(DependencyContextStrings.TypePropertyName);
            bool serviceable = libraryJson.ValueAsBoolean(DependencyContextStrings.ServiceablePropertyName);
            string path = libraryJson.ValueAsString(DependencyContextStrings.PathPropertyName);
            string hashPath = libraryJson.ValueAsString(DependencyContextStrings.HashPathPropertyName);
            string runtimeStoreManifestName = libraryJson.ValueAsString(DependencyContextStrings.RuntimeStoreManifestPropertyName);

            return new LibraryStub()
            {
                Hash = hash,
                Type = Pool(type),
                Serviceable = serviceable,
                Path = path,
                HashPath = hashPath,
                RuntimeStoreManifestName = runtimeStoreManifestName
            };
        }

        private List<RuntimeFallbacks> ReadRuntimes(JsonObject runtimesJson)
        {
            // Object dictionary: string => ["...","...",...]

            var runtimeFallbacks = new List<RuntimeFallbacks>();

            if (runtimesJson != null)
                foreach (var key in runtimesJson.Keys)
                    runtimeFallbacks.Add(new RuntimeFallbacks(key, runtimesJson.ValueAsStringArray(key) ?? EmptyStringArray));

            return runtimeFallbacks;
        }

        private IEnumerable<Library> CreateLibraries(IEnumerable<TargetLibrary> libraries, bool runtime, Dictionary<string, LibraryStub> libraryStubs)
        {
            if (libraries == null)
                return Enumerable.Empty<Library>();

            return libraries.Select(property => CreateLibrary(property, runtime, libraryStubs))
                            .Where(library => library != null);
        }

        private Library CreateLibrary(TargetLibrary targetLibrary, bool runtime, Dictionary<string, LibraryStub> libraryStubs)
        {
            var nameWithVersion = targetLibrary.Name;
            LibraryStub stub;

            if (libraryStubs == null || !libraryStubs.TryGetValue(nameWithVersion, out stub))
            {
                throw new InvalidOperationException($"Cannot find library information for {nameWithVersion}");
            }

            var seperatorPosition = nameWithVersion.IndexOf(DependencyContextStrings.VersionSeperator);

            var name = Pool(nameWithVersion.Substring(0, seperatorPosition));
            var version = Pool(nameWithVersion.Substring(seperatorPosition + 1));

            if (runtime)
            {
                // Runtime section of this library was trimmed by type:platform
                var isCompilationOnly = targetLibrary.CompileOnly;
                if (isCompilationOnly == true)
                {
                    return null;
                }

                var runtimeAssemblyGroups = new List<RuntimeAssetGroup>();
                var nativeLibraryGroups = new List<RuntimeAssetGroup>();
                if (targetLibrary.RuntimeTargets != null)
                {
                    foreach (var ridGroup in targetLibrary.RuntimeTargets.GroupBy(e => e.Rid))
                    {
                        var groupRuntimeAssemblies = ridGroup
                            .Where(e => e.Type == DependencyContextStrings.RuntimeAssetType)
                            .Select(e => e.Path)
                            .ToArray();

                        if (groupRuntimeAssemblies.Any())
                        {
                            runtimeAssemblyGroups.Add(new RuntimeAssetGroup(
                                ridGroup.Key,
                                groupRuntimeAssemblies.Where(a => Path.GetFileName(a) != "_._")));
                        }

                        var groupNativeLibraries = ridGroup
                            .Where(e => e.Type == DependencyContextStrings.NativeAssetType)
                            .Select(e => e.Path)
                            .ToArray();

                        if (groupNativeLibraries.Any())
                        {
                            nativeLibraryGroups.Add(new RuntimeAssetGroup(
                                ridGroup.Key,
                                groupNativeLibraries.Where(a => Path.GetFileName(a) != "_._")));
                        }
                    }
                }

                if (targetLibrary.Runtimes != null && targetLibrary.Runtimes.Count > 0)
                {
                    runtimeAssemblyGroups.Add(new RuntimeAssetGroup(string.Empty, targetLibrary.Runtimes));
                }

                if (targetLibrary.Natives != null && targetLibrary.Natives.Count > 0)
                {
                    nativeLibraryGroups.Add(new RuntimeAssetGroup(string.Empty, targetLibrary.Natives));
                }

                return new RuntimeLibrary(
                    type: stub.Type,
                    name: name,
                    version: version,
                    hash: stub.Hash,
                    runtimeAssemblyGroups: runtimeAssemblyGroups,
                    nativeLibraryGroups: nativeLibraryGroups,
                    resourceAssemblies: targetLibrary.Resources ?? Enumerable.Empty<ResourceAssembly>(),
                    dependencies: targetLibrary.Dependencies,
                    serviceable: stub.Serviceable,
                    path: stub.Path,
                    hashPath: stub.HashPath,
                    runtimeStoreManifestName: stub.RuntimeStoreManifestName);
            }
            else
            {
                var assemblies = (targetLibrary.Compilations != null) ? targetLibrary.Compilations : Enumerable.Empty<string>();
                return new CompilationLibrary(
                    stub.Type,
                    name,
                    version,
                    stub.Hash,
                    assemblies,
                    targetLibrary.Dependencies,
                    stub.Serviceable,
                    stub.Path,
                    stub.HashPath);
            }
        }

        private string Pool(string s)
        {
            if (s == null)
            {
                return null;
            }

            string result;
            if (!_stringPool.TryGetValue(s, out result))
            {
                _stringPool[s] = s;
                result = s;
            }
            return result;
        }

        private class Target
        {
            public string Name;

            public IEnumerable<TargetLibrary> Libraries;
        }

        private struct TargetLibrary
        {
            public string Name;

            public IEnumerable<Dependency> Dependencies;

            public List<string> Runtimes;

            public List<string> Natives;

            public List<string> Compilations;

            public List<RuntimeTargetEntryStub> RuntimeTargets;

            public List<ResourceAssembly> Resources;

            public bool? CompileOnly;
        }

        private struct RuntimeTargetEntryStub
        {
            public string Type;

            public string Path;

            public string Rid;
        }

        private struct LibraryStub
        {
            public string Hash;

            public string Type;

            public bool Serviceable;

            public string Path;

            public string HashPath;

            public string RuntimeStoreManifestName;
        }
    }
}

#endif
