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

namespace Akka.MultiNodeTestRunner
{
#if CORECLR
    public class Discovery : IMessageSink, IDisposable
#else
    public class Discovery : MarshalByRefObject, IMessageSink, IDisposable
#endif
    {
        public Dictionary<string, List<NodeTest>> Tests { get; set; }
        public List<ErrorMessage> Errors { get; } = new List<ErrorMessage>();
        public bool WasSuccessful => Errors.Count == 0;
        
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
            switch (message)
            {
                case ITestCaseDiscoveryMessage testCaseDiscoveryMessage:
                    var testClass = testCaseDiscoveryMessage.TestClass.Class;
                    if (testClass.IsAbstract) return true;
#if CORECLR
                    var specType = testCaseDiscoveryMessage.TestAssembly.Assembly.GetType(testClass.Name).ToRuntimeType();
#else
                    var testAssembly = Assembly.LoadFrom(testCaseDiscoveryMessage.TestAssembly.Assembly.AssemblyPath);
                    var specType = testAssembly.GetType(testClass.Name);
#endif
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
                    break;
                case IDiscoveryCompleteMessage discoveryComplete:
                    Finished.Set();
                    break;
                case ErrorMessage err:
                    Errors.Add(err);
                    break;
            }

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

#if CORECLR
            var ctorWithConfig = configUser
                .GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(c => null != c.GetParameters().FirstOrDefault(p => p.ParameterType.GetTypeInfo().IsSubclassOf(baseConfigType)));
            return ctorWithConfig ?? FindConfigConstructor(configUser.GetTypeInfo().BaseType);
#else
            var ctorWithConfig = configUser
                .GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(c => null != c.GetParameters().FirstOrDefault(p => p.ParameterType.IsSubclassOf(baseConfigType)));
            return ctorWithConfig ?? FindConfigConstructor(configUser.BaseType);
#endif
        }

        private object[] ConfigConstructorParamValues(Type configType)
        {
            var ctors = configType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            var empty = ctors.FirstOrDefault(c => !c.GetParameters().Any());

#if CORECLR
            return empty != null
                ? new object[0]
                : ctors.First().GetParameters().Select(p => p.ParameterType.GetTypeInfo().IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
#else
            return empty != null
                ? new object[0]
                : ctors.First().GetParameters().Select(p => p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
#endif
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Finished.Dispose();
        }
    }
}