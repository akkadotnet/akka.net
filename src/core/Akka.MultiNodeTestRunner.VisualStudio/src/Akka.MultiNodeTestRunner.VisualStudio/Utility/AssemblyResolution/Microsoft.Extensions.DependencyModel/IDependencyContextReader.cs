#if NETCOREAPP

using System;
using System.IO;

namespace Internal.Microsoft.Extensions.DependencyModel
{
    internal interface IDependencyContextReader: IDisposable
    {
        DependencyContext Read(Stream stream);
    }
}

#endif
