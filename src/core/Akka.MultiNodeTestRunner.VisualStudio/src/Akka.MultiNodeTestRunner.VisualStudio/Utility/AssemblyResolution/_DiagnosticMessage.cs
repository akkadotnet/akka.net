using Xunit.Abstractions;

namespace Xunit
{
    class _DiagnosticMessage : IDiagnosticMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DiagnosticMessage"/> class.
        /// </summary>
        public _DiagnosticMessage() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="DiagnosticMessage"/> class.
        /// </summary>
        /// <param name="message">The message to send</param>
        public _DiagnosticMessage(string message)
        {
            Message = message;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DiagnosticMessage"/> class.
        /// </summary>
        /// <param name="format">The format of the message to send</param>
        /// <param name="args">The arguments used to format the message</param>
        public _DiagnosticMessage(string format, params object[] args)
        {
            Message = string.Format(format, args);
        }

        /// <inheritdoc/>
        public string Message { get; set; }
    }
}
