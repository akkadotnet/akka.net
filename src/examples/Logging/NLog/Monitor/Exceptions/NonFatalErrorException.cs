using System;

namespace Monitor.Exceptions
{
    /// <summary>
    /// A mock-exception simulating conditions when a non-fatal exception has occured
    /// </summary>
    public class NonFatalErrorException : Exception
    {
        public NonFatalErrorException(string message)
            : base(message)
        { }

        public NonFatalErrorException()
            : base()
        { }
    }
}