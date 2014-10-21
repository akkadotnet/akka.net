using System;

namespace Monitor.Exceptions
{
    /// <summary>
    /// A mock-exception simulating conditions when a fatal exception has occured
    /// </summary>
    public class FatalErrorException : Exception
    {
        public FatalErrorException(string message)
            : base(message)
        { }

        public FatalErrorException()
            : base()
        { }
    }
}