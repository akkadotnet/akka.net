using System.IO;
using System.Reflection;
using System.Text;
using Akka.MultiNodeTestRunner.Shared.Reporting;
using Akka.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Akka.MultiNodeTestRunner.Shared.Persistence
{
    /// <summary>
    /// XML (omg not XML!) implementation of the <see cref="IPersistentTestRunStore"/>
    /// </summary>
    public class JsonPersistentTestRunStore : IPersistentTestRunStore
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
            Guard.Assert(data != null, "TestRunTree must not be null.");
            Guard.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");


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
            Guard.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");
            // ReSharper disable once AssignNullToNotNullAttribute //already made this null check with Guard
            var finalPath = Path.GetFullPath(filePath);
            var fileText = File.ReadAllText(finalPath, Encoding.UTF8);
            if (string.IsNullOrEmpty(fileText)) return null;
            return JsonConvert.DeserializeObject<TestRunTree>(fileText, Settings);
        }
    }
}