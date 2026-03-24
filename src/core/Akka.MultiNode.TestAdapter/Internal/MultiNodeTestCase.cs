using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Akka.Remote.TestKit;
using Xunit.Abstractions;
using Xunit.Sdk;
using TestMethodDisplay = Xunit.Sdk.TestMethodDisplay;
using TestMethodDisplayOptions = Xunit.Sdk.TestMethodDisplayOptions;

#nullable enable
namespace Akka.MultiNode.TestAdapter.Internal
{
    public class MultiNodeTestCase : XunitTestCase
    {
        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
        public MultiNodeTestCase() { }

        public MultiNodeTestCase(
            IMessageSink diagnosticMessageSink,
            TestMethodDisplay defaultMethodDisplay,
            TestMethodDisplayOptions defaultMethodDisplayOptions,
            ITestMethod testMethod,
            object[]? testMethodArguments = null)
            : base(
                diagnosticMessageSink,
                defaultMethodDisplay,
                defaultMethodDisplayOptions,
                testMethod,
                testMethodArguments)
        { }
        
        public virtual string? AssemblyPath { get; protected set; }
        public virtual string TypeName => TestMethod.TestClass.Class.Name;
        public virtual string MethodName => TestMethod.Method.Name;

        protected List<NodeTest>? InternalNodes;

        /// <exception cref="TestBaseTypeException">Spec did not inherit from <see cref="MultiNodeSpec"/></exception>
        /// <exception cref="TestConfigurationException">Invalid configuration class</exception>
        public List<NodeTest> Nodes
        {
            get
            {
                EnsureInitialized();
                return InternalNodes ?? new List<NodeTest>();
            }
        }

        private string? _skipReason;
        public new string? SkipReason
        {
            get => _skipReason ?? base.SkipReason;
            set => _skipReason = value;
        }
        
        public bool InExecutionMode { get; set; }

        protected override void Initialize()
        {
            base.Initialize();
            try
            {
                AssemblyPath = Path.GetFullPath(TestMethod.TestClass.Class.Assembly.AssemblyPath);
                InternalNodes = LoadDetails();
            }
            catch (Exception e)
            {
                SkipReason = e.ToString();
                //InitializationException = e;
                DisplayName = $"{BaseDisplayName}(???)";
            }
        }

        internal void Load()
        {
            EnsureInitialized();
        }

        public override Task<RunSummary> RunAsync(
            IMessageSink diagnosticMessageSink,
            IMessageBus messageBus,
            object[] constructorArguments,
            ExceptionAggregator aggregator,
            CancellationTokenSource cancellationTokenSource)
        {
            if (!InExecutionMode)
            {
                return new MultiNodeTestCaseRunner(this, DisplayName, SkipReason, messageBus, diagnosticMessageSink, 
                    aggregator, cancellationTokenSource).RunAsync();
            }
            return new XunitTestCaseRunner(this, DisplayName, SkipReason, constructorArguments, TestMethodArguments, messageBus, aggregator, cancellationTokenSource).RunAsync();
        }

        public override void Serialize(IXunitSerializationInfo data)
        {
            base.Serialize(data);
            data.AddValue(nameof(AssemblyPath), AssemblyPath);
            data.AddValue(nameof(_skipReason), _skipReason);
        }

        public override void Deserialize(IXunitSerializationInfo data)
        {
            base.Deserialize(data);
            AssemblyPath = data.GetValue<string>(nameof(AssemblyPath));
            _skipReason = data.GetValue<string>(nameof(_skipReason));
        }

        /// <exception cref="TestBaseTypeException">Spec did not inherit from <see cref="MultiNodeSpec"/></exception>
        /// <exception cref="TestConfigurationException">Invalid configuration class</exception>
        protected virtual List<NodeTest> LoadDetails()
        {
            var specType = TestMethod.TestClass.Class.Assembly.GetType(TypeName).ToRuntimeType();
            if (!typeof(MultiNodeSpec).IsAssignableFrom(specType))
            {
                throw new TestBaseTypeException();
            }

            try
            {
                var roles = RoleNames(specType);
                return roles.Select((r, i) => new NodeTest(this, i + 1, r.Name)).ToList();
            }
            catch (Exception e)
            {
                SkipReason = e.ToString();
                return new List<NodeTest>
                {
                    new ErrorTest(this)
                };
            }
        }
        
        private IEnumerable<RoleName> RoleNames(Type specType)
        {
            var ctorWithConfig = FindConfigConstructor(specType);
            try
            {
                var configType = ctorWithConfig.GetParameters().First().ParameterType;
                var args = ConfigConstructorParamValues(configType);
                var configInstance = (MultiNodeConfig) Activator.CreateInstance(configType, args);
                return configInstance.Roles;
            }
            catch (Exception e)
            {
                throw new TestConfigurationConstructorException(specType, e);
            }
        }
        
        internal static ConstructorInfo FindConfigConstructor(Type configUser)
        {
            var baseConfigType = typeof(MultiNodeConfig);
            var current = configUser;
            while (current != null)
            {
                var ctorWithConfig = current
                    .GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(c => null != c.GetParameters().FirstOrDefault(p => p.ParameterType.GetTypeInfo().IsSubclassOf(baseConfigType)));
            
                current = current.GetTypeInfo().BaseType;
                if (ctorWithConfig != null) return ctorWithConfig;
            }

            throw new TestConfigurationException(configUser);
        }

        private object?[] ConfigConstructorParamValues(Type configType)
        {
            var ctors = configType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            var empty = ctors.FirstOrDefault(c => !c.GetParameters().Any());

            return empty != null
                ? Array.Empty<object>()
                : ctors.First().GetParameters().Select(p => p.ParameterType.GetTypeInfo().IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
        }
    }
}