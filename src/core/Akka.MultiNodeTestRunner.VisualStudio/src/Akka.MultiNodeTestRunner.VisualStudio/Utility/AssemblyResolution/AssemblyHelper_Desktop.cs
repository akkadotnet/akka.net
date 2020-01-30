#if NETFRAMEWORK

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Xunit
{
    /// <summary>
    /// This class provides assistance with assembly resolution for missing assemblies.
    /// </summary>
    class AssemblyHelper : LongLivedMarshalByRefObject, IDisposable
    {
        static readonly string[] Extensions = { ".dll", ".exe" };

        readonly string directory;
        readonly IMessageSink internalDiagnosticsMessageSink;
        readonly Dictionary<string, Assembly> lookupCache = new Dictionary<string, Assembly>();

        /// <summary>
        /// Constructs an instance using the given <paramref name="directory"/> for resolution.
        /// </summary>
        /// <param name="directory">The directory to use for resolving assemblies.</param>
        public AssemblyHelper(string directory) : this(directory, null) { }

        /// <summary>
        /// Constructs an instance using the given <paramref name="directory"/> for resolution.
        /// </summary>
        /// <param name="directory">The directory to use for resolving assemblies.</param>
        /// <param name="internalDiagnosticsMessageSink">The message sink to send internal diagnostics messages to</param>
        public AssemblyHelper(string directory, IMessageSink internalDiagnosticsMessageSink)
        {
            this.directory = directory;
            this.internalDiagnosticsMessageSink = internalDiagnosticsMessageSink;

            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        /// <inheritdoc/>
        public void Dispose()
            => AppDomain.CurrentDomain.AssemblyResolve -= Resolve;

        Assembly LoadAssembly(AssemblyName assemblyName)
        {
            if (lookupCache.TryGetValue(assemblyName.Name, out var result))
                return result;

            var path = Path.Combine(directory, assemblyName.Name);
            result = ResolveAndLoadAssembly(path, out var resolvedAssemblyPath);

            if (internalDiagnosticsMessageSink != null)
            {
                if (result == null)
                    internalDiagnosticsMessageSink.OnMessage(new _DiagnosticMessage($"[AssemblyHelper_Desktop.LoadAssembly] Resolution for '{assemblyName.Name}' failed, passed down to next resolver"));
                else
                    internalDiagnosticsMessageSink.OnMessage(new _DiagnosticMessage($"[AssemblyHelper_Desktop.LoadAssembly] Resolved '{assemblyName.Name}' to '{resolvedAssemblyPath}'"));
            }

            lookupCache[assemblyName.Name] = result;
            return result;
        }

        Assembly Resolve(object sender, ResolveEventArgs args)
            => LoadAssembly(new AssemblyName(args.Name));

        Assembly ResolveAndLoadAssembly(string pathWithoutExtension, out string resolvedAssemblyPath)
        {
            foreach (var extension in Extensions)
            {
                resolvedAssemblyPath = pathWithoutExtension + extension;

                try
                {
                    if (File.Exists(resolvedAssemblyPath))
                        return Assembly.LoadFrom(resolvedAssemblyPath);
                }
                catch { }
            }

            resolvedAssemblyPath = null;
            return null;
        }

        /// <summary>
        /// Subscribes to the appropriate assembly resolution event, to provide automatic assembly resolution for
        /// an assembly and any of its dependencies. Depending on the target platform, this may include the use
        /// of the .deps.json file generated during the build process.
        /// </summary>
        /// <returns>An object which, when disposed, un-subscribes.</returns>
        public static IDisposable SubscribeResolveForAssembly(string assemblyFileName, IMessageSink internalDiagnosticsMessageSink = null)
            => new AssemblyHelper(Path.GetDirectoryName(Path.GetFullPath(assemblyFileName)), internalDiagnosticsMessageSink);

        /// <summary>
        /// Subscribes to the appropriate assembly resolution event, to provide automatic assembly resolution for
        /// an assembly and any of its dependencies. Depending on the target platform, this may include the use
        /// of the .deps.json file generated during the build process.
        /// </summary>
        /// <returns>An object which, when disposed, un-subscribes.</returns>
        public static IDisposable SubscribeResolveForAssembly(Type typeInAssembly, IMessageSink internalDiagnosticsMessageSink = null)
            => new AssemblyHelper(Path.GetDirectoryName(typeInAssembly.Assembly.Location), internalDiagnosticsMessageSink);
    }
}

#endif
