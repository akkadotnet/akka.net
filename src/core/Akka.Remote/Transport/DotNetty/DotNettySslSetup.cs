//-----------------------------------------------------------------------
// <copyright file="DotNettySslSetup.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Security.Cryptography.X509Certificates;
using Akka.Actor.Setup;

namespace Akka.Remote.Transport.DotNetty;

public sealed class DotNettySslSetup: Setup
{
    public DotNettySslSetup(X509Certificate2 certificate, bool suppressValidation)
    {
        Certificate = certificate;
        SuppressValidation = suppressValidation;
    }
    
    public X509Certificate2 Certificate { get; }
    public bool SuppressValidation { get; }

    internal SslSettings Settings => new SslSettings(Certificate, SuppressValidation);
}
