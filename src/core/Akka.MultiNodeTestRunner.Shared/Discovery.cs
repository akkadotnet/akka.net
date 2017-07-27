//-----------------------------------------------------------------------
// <copyright file="Discovery.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Akka.MultiNodeTestRunner.Shared;
using Akka.Remote.TestKit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Akka.MultiNodeTestRunner.Shared
{
    public class Discovery : MarshalByRefObject, IMessageSink, IDisposable
    {
        public Dictionary<string, List<NodeTest>> Tests { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Discovery"/> class.
        /// </summary>
        public Discovery()
        {
            Tests = new Dictionary<string, List<NodeTest>>();
            Finished = new ManualResetEvent(false);
        }

        public ManualResetEvent Finished { get; private set; }

        public IMessageSink NextSink { get; private set; }

        public virtual bool OnMessage(IMessageSinkMessage message)
        {
            var testCaseDiscoveryMessage = message as ITestCaseDiscoveryMessage;
            if (testCaseDiscoveryMessage != null)
            {
                var testClass = testCaseDiscoveryMessage.TestClass.Class;
                if (testClass.IsAbstract) return true;
                var testAssembly = Assembly.LoadFrom(testCaseDiscoveryMessage.TestAssembly.Assembly.AssemblyPath);
                var specType = testAssembly.GetType(testClass.Name);
                var roles = RoleNames(specType);
                
                var details = roles.Select((r, i) => new NodeTest
                {
                    Node = i + 1,
                    Role = r.Name,
                    TestName = testClass.Name,
                    TypeName = testClass.Name,
                    MethodName = testCaseDiscoveryMessage.TestCase.TestMethod.Method.Name,
                    SkipReason = testCaseDiscoveryMessage.TestCase.SkipReason,
                }).ToList();
                if (details.Any())
                    Tests.Add(details.First().TestName, details);

            }

            if (message is IDiscoveryCompleteMessage)
                Finished.Set();

            return true;
        }

        private IEnumerable<RoleName> RoleNames(Type specType)
        {
            var ctorWithConfig = FindConfigConstructor(specType);
            var configType = ctorWithConfig.GetParameters().First().ParameterType;
            var args = ConfigConstructorParamValues(configType);
            var configInstance = Activator.CreateInstance(configType, args);
            var roleType = typeof(RoleName);
            var configProps = configType.GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public);
            var roleProps = configProps.Where(p => p.PropertyType == roleType).Select(p => (RoleName)p.GetValue(configInstance));
            var configFields = configType.GetFields(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public);
            var roleFields = configFields.Where(f => f.FieldType == roleType).Select(f => (RoleName)f.GetValue(configInstance));
            var roles = roleProps.Concat(roleFields).Distinct();
            return roles;
        }

        private ConstructorInfo FindConfigConstructor(Type configUser)
        {
            var baseConfigType = typeof(MultiNodeConfig);
            
            var ctorWithConfig = configUser
                .GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(c => null != c.GetParameters().FirstOrDefault(p => p.ParameterType.IsSubclassOf(baseConfigType)));
            return ctorWithConfig ?? FindConfigConstructor(configUser.BaseType);
        }

        private object[] ConfigConstructorParamValues(Type configType)
        {
            var ctors = configType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            var empty = ctors.FirstOrDefault(c => !c.GetParameters().Any());

            return empty != null
                ? new object[0]
                : ctors.First().GetParameters().Select(p => p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Finished.Dispose();
        }
    }
}