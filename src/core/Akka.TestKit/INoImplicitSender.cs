//-----------------------------------------------------------------------
// <copyright file="INoImplicitSender.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.TestKit
{
    /// <summary>
    /// Normally test classes has <see cref="TestKitBase.TestActor"/> as implicit sender.
    /// So when no sender is specified when sending messages, <see cref="TestKitBase.TestActor"/>
    /// is used.
    /// When a a test class implements <see cref="INoImplicitSender"/> this behavior is removed and the normal
    /// behavior is restored, i.e. <see cref="ActorRefs.NoSender"/> is used as sender when no sender has been specified.
    /// <example>
    /// <code>
    /// public class WithImplicitSender : TestKit
    /// {
    ///    public void TheTestMethod()
    ///    {
    ///       ...
    ///       someActor.Tell("message");             //TestActor is used as Sender
    ///       someActor.Tell("message", TestActor);  //TestActor is used as Sender
    ///    }
    /// }
    /// 
    /// public class WithNoImplicitSender : TestKit, INoImplicitSender
    /// {
    ///    public void TheTestMethod()
    ///    {
    ///       ...
    ///       someActor.Tell("message");    //ActorRefs.NoSender is used as Sender
    ///    }
    /// }
    /// </code>
    /// </example>
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public interface INoImplicitSender
    {
    }
}
