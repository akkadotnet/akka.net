using System;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace Akka.MultiNode.TestAdapter.Helpers
{
    /// <summary>
    /// RuntimeConfigGenerator
    /// </summary>
    internal static class RuntimeConfigGenerator
    {
        /// <summary>
        /// Generates .NET Core runtimeconfig.json content for current target framework and runtime
        /// </summary>
        public static string GetRuntimeConfigContent(Assembly assembly)
        {
            var version = Environment.Version.ToString(); // Something like 3.1.1
            var framework = assembly.GetCustomAttribute<TargetFrameworkAttribute>();
            var frameworkName = framework?.FrameworkName ?? ".NETCoreApp,Version=v3.0"; // Something like .NETCoreApp,Version=v3.0
            var majorFrameworkVersion = Regex.Match(frameworkName, @"\d\.\d").Value;

            return $@"
{{
    ""runtimeOptions"": {{
        ""tfm"": ""netcoreapp{majorFrameworkVersion}"",
        ""framework"": {{
            ""name"": ""Microsoft.NETCore.App"",
            ""version"": ""{version}""
        }}
    }}
}}";
        }
    }
}