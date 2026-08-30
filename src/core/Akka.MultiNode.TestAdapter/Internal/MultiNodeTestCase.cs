using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Akka.Remote.TestKit;
using Xunit.Sdk;
using Xunit.v3;

#nullable enable
namespace Akka.MultiNode.TestAdapter.Internal
{
    public class MultiNodeTestCase : XunitTestCase, ISelfExecutingXunitTestCase
    {
        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
        public MultiNodeTestCase() { }

        public MultiNodeTestCase(
            IXunitTestMethod testMethod,
            string testCaseDisplayName,
            string uniqueID,
            bool @explicit,
            string? skipReason = null,
            Type[]? skipExceptions = null,
            Type? skipType = null,
            string? skipUnless = null,
            string? skipWhen = null,
            string? sourceFilePath = null,
            int? sourceLineNumber = null,
            int? timeout = null)
            : base(
                testMethod,
                testCaseDisplayName,
                uniqueID,
                @explicit,
                skipExceptions,
                skipReason,
                skipType,
                skipUnless,
                skipWhen,
                traits: null,
                testMethodArguments: null,
                sourceFilePath: sourceFilePath,
                sourceLineNumber: sourceLineNumber,
                timeout: timeout)
        { }

        public virtual string? AssemblyPath { get; protected set; }
        public virtual string TypeName => TestMethod.TestClass.Class.FullName!;
        public virtual string MethodName => TestMethod.Method.Name;

        protected List<NodeTest>? InternalNodes;

        public Exception? InitializationException { get; protected set; }

        /// <exception cref="TestBaseTypeException">Spec did not inherit from <see cref="MultiNodeSpec"/></exception>
        /// <exception cref="TestConfigurationException">Invalid configuration class</exception>
        public List<NodeTest> Nodes
        {
            get
            {
                if (InternalNodes == null)
                    Load();
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

        internal void Load()
        {
            try
            {
                AssemblyPath = Path.GetFullPath(TestMethod.TestClass.Class.Assembly.Location);
                InternalNodes = LoadDetails();
            }
            catch (Exception e)
            {
                SkipReason = e.ToString();
                InitializationException = e;
            }
        }

        public ValueTask<RunSummary> Run(
            ExplicitOption explicitOption,
            IMessageBus messageBus,
            object?[] constructorArguments,
            ExceptionAggregator aggregator,
            CancellationTokenSource cancellationTokenSource)
        {
            if (!InExecutionMode)
            {
                return new MultiNodeTestCaseRunner(
                    this, TestCaseDisplayName, SkipReason, messageBus,
                    aggregator, cancellationTokenSource).RunAsync();
            }

            // In execution mode (node process), delegate to standard xUnit runner
            var tests = CreateTests().GetAwaiter().GetResult();
            return XunitTestCaseRunner.Instance.Run(
                this,
                tests,
                messageBus,
                aggregator,
                cancellationTokenSource,
                TestCaseDisplayName,
                SkipReason,
                explicitOption,
                constructorArguments);
        }

        protected override void Serialize(IXunitSerializationInfo info)
        {
            base.Serialize(info);
            info.AddValue(nameof(AssemblyPath), AssemblyPath);
            info.AddValue(nameof(_skipReason), _skipReason);
        }

        protected override void Deserialize(IXunitSerializationInfo info)
        {
            base.Deserialize(info);
            AssemblyPath = info.GetValue<string>(nameof(AssemblyPath));
            _skipReason = info.GetValue<string>(nameof(_skipReason));
        }

        /// <exception cref="TestBaseTypeException">Spec did not inherit from <see cref="MultiNodeSpec"/></exception>
        /// <exception cref="TestConfigurationException">Invalid configuration class</exception>
        protected virtual List<NodeTest> LoadDetails()
        {
            var specType = TestMethod.TestClass.Class;
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
                var configInstance = (MultiNodeConfig) Activator.CreateInstance(configType, args)!;
                return configInstance!.Roles;
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
