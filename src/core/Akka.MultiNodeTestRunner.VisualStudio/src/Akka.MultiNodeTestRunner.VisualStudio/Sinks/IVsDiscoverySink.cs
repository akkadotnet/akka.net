using System;

namespace Xunit.Runner.VisualStudio
{
    internal interface IVsDiscoverySink : IMessageSinkWithTypes, IDisposable
    {
        int Finish();
    }
}
