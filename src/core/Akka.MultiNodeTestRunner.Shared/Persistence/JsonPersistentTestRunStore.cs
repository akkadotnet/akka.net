//-----------------------------------------------------------------------
// <copyright file="JsonPersistentTestRunStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Reflection;
using System.Text;
using Akka.MultiNodeTestRunner.Shared.Reporting;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Akka.MultiNodeTestRunner.Shared.Persistence
{
    /// <summary>
    /// JavaScript Object Notation (JSON) implementation of the <see cref="IRetrievableTestRunStore"/>
    /// </summary>
    public class JsonPersistentTestRunStore : IRetrievableTestRunStore
    {
        //Internal version of the contract resolver
        private class AkkaContractResolver : DefaultContractResolver
        {
            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                var prop = base.CreateProperty(member, memberSerialization);

                if (!prop.Writable)
                {
                    var property = member as PropertyInfo;
                    if (property != null)
                    {
                        var hasPrivateSetter = property.GetSetMethod(true) != null;
                        prop.Writable = hasPrivateSetter;
                    }
                }

                return prop;
            }
        }

        public static JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            PreserveReferencesHandling = PreserveReferencesHandling.Objects,
            DefaultValueHandling = DefaultValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            //important: if reuse, the serializer will overwrite properties in default references, e.g. Props.DefaultDeploy or Props.noArgs
            ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
            TypeNameHandling = TypeNameHandling.All,
            ContractResolver = new AkkaContractResolver()
            {
                //SerializeCompilerGeneratedMembers = true,
                //IgnoreSerializableAttribute = true,
                //IgnoreSerializableInterface = true,
            }
        };

        public bool SaveTestRun(string filePath, TestRunTree data)
        {
            if (data == null) throw new ArgumentNullException("data");
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentException("filePath must not be null or empty");


// ReSharper disable once AssignNullToNotNullAttribute //already made this null check with Guard
            var finalPath = Path.GetFullPath(filePath);
            var serializedObj = JsonConvert.SerializeObject(data, Formatting.Indented, Settings);

// ReSharper disable once AssignNullToNotNullAttribute
            File.WriteAllText(finalPath, serializedObj, Encoding.UTF8);

            return true;
        }

        public bool TestRunExists(string filePath)
        {
            return !string.IsNullOrEmpty(filePath) && File.Exists(Path.GetFullPath(filePath));
        }

        public TestRunTree FetchTestRun(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentException("filePath must not be null or empty");
            // ReSharper disable once AssignNullToNotNullAttribute //already made this null check with Guard
            var finalPath = Path.GetFullPath(filePath);
            var fileText = File.ReadAllText(finalPath, Encoding.UTF8);
            if (string.IsNullOrEmpty(fileText)) return null;
            return JsonConvert.DeserializeObject<TestRunTree>(fileText, Settings);
        }
    }
}

