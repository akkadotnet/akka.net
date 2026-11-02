// -----------------------------------------------------------------------
// <copyright file="ConfigReader.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.IO;
using Newtonsoft.Json;

namespace Akka.MultiNode.TestAdapter.Configuration
{
    public static class OptionsReader
    {
        public static MultiNodeTestRunnerOptions Load(string assemblyFileName)
        {
            var assemblyName = Path.GetFileNameWithoutExtension(assemblyFileName);
            var directoryName = Path.GetDirectoryName(assemblyFileName) ?? "";
            return LoadFile(Path.Combine(directoryName, $"{assemblyName}.xunit.multinode.runner.json"))
                   ?? LoadFile(Path.Combine(directoryName, "xunit.multinode.runner.json")) 
                   ?? MultiNodeTestRunnerOptions.Default;
        }

        private static MultiNodeTestRunnerOptions LoadFile(string configFileName)
        {
            try
            {
                if(!File.Exists(configFileName))
                {
                    return null;
                }                
                using var stream = File.OpenRead(configFileName);
                return Load(stream);
            }
            catch
            { }
            return null;
        }

        private static MultiNodeTestRunnerOptions Load(Stream configStream)
        {
            var result = new MultiNodeTestRunnerOptions();
            try
            {
                using (var reader = new StreamReader(configStream))
                {
                    var config = (JsonObject) JsonDeserializer.Deserialize(reader);
                    foreach (var propertyName in config.Keys)
                    {
                        var propertyValue = config.Value(propertyName);

                        switch (propertyValue)
                        {
                            case JsonBoolean booleanValue:
                                if (string.Equals(propertyName, Configuration.AppendLogOutput, StringComparison.OrdinalIgnoreCase))
                                    result.AppendLogOutput = booleanValue.Value;
                                if (string.Equals(propertyName, Configuration.UseBuiltInTrxReporter, StringComparison.OrdinalIgnoreCase))
                                    result.UseBuiltInTrxReporter = booleanValue.Value;
                                break;
                            
                            case JsonString stringValue:
                                if (string.Equals(propertyName, Configuration.OutputDirectory, StringComparison.OrdinalIgnoreCase))
                                    result.OutputDirectory = stringValue.Value;
                                if (string.Equals(propertyName, Configuration.FailedSpecsDirectory, StringComparison.OrdinalIgnoreCase))
                                    result.FailedSpecsDirectory = stringValue.Value;
                                if(string.Equals(propertyName, Configuration.ListenAddress, StringComparison.OrdinalIgnoreCase))
                                    result.ListenAddress = stringValue.Value;
                                break;
                            
                            case JsonNumber numberValue when string.Equals(propertyName, Configuration.ListenPort, StringComparison.OrdinalIgnoreCase):
                                int.TryParse(numberValue.Raw, out var port);
                                if(port != 0)
                                    result.ListenPort = port;
                                break;
                        }
                    }
                }
            }
            catch { }

            return result;
        }
        
        static class Configuration
        {
            public const string OutputDirectory = "outputDirectory";
            public const string FailedSpecsDirectory = "failedSpecsDirectory";
            public const string ListenAddress = "listenAddress";
            public const string ListenPort = "listenPort";
            public const string AppendLogOutput = "appendLogOutput";
            public const string UseBuiltInTrxReporter = "useBuiltInTrxReporter";
        }        
    }
}