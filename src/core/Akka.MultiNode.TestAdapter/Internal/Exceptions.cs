using System;
using Akka.Remote.TestKit;

namespace Akka.MultiNode.TestAdapter.Internal
{
    public class TestConfigurationException: Exception
    {
        public TestConfigurationException(Type type)
            : base(
                $"[{type}] or one of its base classes must specify constructor, " +
                $"which first parameter is a subclass of {typeof(MultiNodeConfig)}")
        { }

        public TestConfigurationException(Type type, Exception innerException)
            : base(
                $"[{type}] or one of its base classes must specify constructor, " +
                $"which first parameter is a subclass of {typeof(MultiNodeConfig)}", 
                innerException)
        { }
    }
    
    public class TestConfigurationConstructorException: Exception
    {
        public TestConfigurationConstructorException(Type type)
            : base($"[{type}] constructor, which is a subclass of {typeof(MultiNodeConfig)}, throws an exception")
        { }

        public TestConfigurationConstructorException(Type type, Exception innerException)
            : base(
                $"[{type}] constructor, which is a subclass of {typeof(MultiNodeConfig)}, throws an exception", 
                innerException)
        { }
    }

    public class TestBaseTypeException: Exception
    {
        public TestBaseTypeException() 
            : base($"MultiNode.TestRunner spec should inherit from {typeof(MultiNodeSpec).FullName}")
        { }
    }
    
    internal class TestFailedException : Exception
    {
        private readonly string _stackTrace;

        public TestFailedException(string type, string message, string stacktrace):base($"Original exception: [{type}: {message}]")
        {
            _stackTrace = stacktrace;
        }

        public TestFailedException(string type, string message, string stacktrace, Exception innerException)
            : base($"Original exception: [{type}: {message}]", innerException)
        {
            _stackTrace = stacktrace;
        }
        
        public override string StackTrace => _stackTrace ?? base.StackTrace;
    }
    
}