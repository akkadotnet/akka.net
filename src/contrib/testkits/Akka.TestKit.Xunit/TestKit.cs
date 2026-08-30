//-----------------------------------------------------------------------
// <copyright file="TestKit.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit.Xunit.Attributes;
using Akka.TestKit.Xunit.Internals;
using Xunit;

namespace Akka.TestKit.Xunit;

/// <summary>
/// This class represents an Akka.NET TestKit that uses <a href="https://xunit.github.io/">xUnit</a>
/// as its testing framework.
/// </summary>
[AkkaCleanAmbientContext]
public class TestKit : TestKitBase, IDisposable
{
    private class PrefixedOutput : ITestOutputHelper
    {
        private static readonly string Newline = Environment.NewLine;
        private readonly ITestOutputHelper _output;
        private readonly string _prefix;
        private bool _newLine;

        public PrefixedOutput(ITestOutputHelper output, string prefix)
        {
            _output = output;
            _prefix = prefix;
        }

        public void Write(string message)
        {
            if(_newLine)
                message = _prefix + Newline;
                
            var parts = message.Split([Newline], StringSplitOptions.None);
            _output.Write(string.Join($"{Newline}{_prefix}", parts));
            _newLine = message.EndsWith(Newline);
        }

        public void Write(string format, params object[] args)
        {
            if(_newLine)
                format = _prefix + format;
            var parts = format.Split([Newline], StringSplitOptions.None);
            format = string.Join($"{Newline}{_prefix}", parts);
                
            _output.Write(format, args);
            _newLine = format.EndsWith(Newline);
        }

        public void WriteLine(string message)
        {
            if (_newLine)
                message = _prefix + message;
            _newLine = true;
            _output.WriteLine(message);
        }

        public void WriteLine(string format, params object[] args)
        {
            if (_newLine)
                format = _prefix + format;
            _newLine = true;
            _output.WriteLine(format, args);
        }

        public string Output => _output.Output;
    }

    /// <summary>
    /// The provider used to write test output.
    /// </summary>
    protected readonly ITestOutputHelper? Output;

    private bool _disposed;
    private bool _disposing;
        
    /// <summary>
    /// <para>
    /// Initializes a new instance of the <see cref="TestKit"/> class.
    /// </para>
    /// <para>
    /// If no <paramref name="system"/> is passed in, a new system with
    /// <see cref="DefaultConfig"/> will be created.
    /// </para>
    /// </summary>
    /// <param name="system">The actor system to use for testing. The default value is <see langword="null"/>.</param>
    /// <param name="output">The provider used to write test output. The default value is <see langword="null"/>.</param>
    public TestKit(ActorSystem? system = null, ITestOutputHelper? output = null)
        : base(Assertions, system)
    {
        Output = output;
        InitializeLogger(Sys);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TestKit"/> class.
    /// </summary>
    /// <param name="config">The <see cref="Setup"/> to use for configuring the ActorSystem.</param>
    /// <param name="actorSystemName">The name of the system. The default name is "test".</param>
    /// <param name="output">The provider used to write test output. The default value is <see langword="null"/>.</param>
    public TestKit(ActorSystemSetup config, string? actorSystemName = null, ITestOutputHelper? output = null)
        : base(Assertions, config, actorSystemName)
    {
        Output = output;
        InitializeLogger(Sys);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TestKit"/> class.
    /// </summary>
    /// <param name="config">The configuration to use for the system.</param>
    /// <param name="actorSystemName">The name of the system. The default name is "test".</param>
    /// <param name="output">The provider used to write test output. The default value is <see langword="null"/>.</param>
    public TestKit(Config config, string? actorSystemName = null, ITestOutputHelper? output = null)
        : base(Assertions, config, actorSystemName)
    {
        Output = output;
        InitializeLogger(Sys);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TestKit"/> class.
    /// </summary>
    /// <param name="config">The configuration to use for the system.</param>
    /// <param name="output">The provider used to write test output. The default value is <see langword="null"/>.</param>
    public TestKit(string config, ITestOutputHelper? output = null)
        : base(Assertions, ConfigurationFactory.ParseString(config))
    {
        Output = output;
        InitializeLogger(Sys);
    }

    /// <summary>
    /// Ensures the implicit sender (<see cref="InternalCurrentActorCellKeeper.Current"/>) is set
    /// on the current thread. This is needed because the <c>[ThreadStatic]</c> value can be lost
    /// when <c>IAsyncLifetime.InitializeAsync</c> contains an <c>await</c> that causes a thread switch.
    /// Called automatically when accessing <see cref="Sys"/> or <see cref="TestActor"/>.
    /// </summary>
    private void EnsureImplicitSender()
    {
        // Only set the implicit sender if Current is not already set.
        // During actor message processing, Mailbox.Run() sets Current to the actor's cell.
        // Overwriting it here would corrupt the actor context for the rest of that mailbox run.
        if (this is not INoImplicitSender && InternalCurrentActorCellKeeper.Current == null)
            InternalCurrentActorCellKeeper.Current = (ActorCell)((ActorRefWithCell)base.TestActor).Underlying;
    }

    /// <summary>
    /// The <see cref="ActorSystem"/> used for testing. Accessing this property ensures the
    /// implicit sender is set on the current thread.
    /// </summary>
    public new ActorSystem Sys
    {
        get
        {
            EnsureImplicitSender();
            return base.Sys;
        }
    }

    /// <summary>
    /// The default test actor. Accessing this property ensures the implicit sender
    /// is set on the current thread.
    /// </summary>
    public new IActorRef TestActor
    {
        get
        {
            EnsureImplicitSender();
            return base.TestActor;
        }
    }

    /// <summary>
    /// A configuration that has just the default log settings enabled. The default settings can be found in
    /// <a href="https://github.com/akkadotnet/akka.net/blob/master/src/core/Akka.TestKit/Internal/Reference.conf">Akka.TestKit.Internal.Reference.conf</a>.
    /// </summary>
    public new static Config DefaultConfig => TestKitBase.DefaultConfig;

    /// <summary>
    /// A configuration that has all log settings enabled
    /// </summary>
    public new static Config FullDebugConfig => TestKitBase.FullDebugConfig;

    /// <summary>
    /// Commonly used assertions used throughout the testkit.
    /// </summary>
    protected static XunitAssertions Assertions { get; } = new();

    /// <summary>
    /// This method is called when a test ends.
    /// </summary>
    protected virtual void AfterAll()
    {
    }

    /// <summary>
    /// Initializes a new <see cref="TestOutputLogger"/> used to log messages.
    /// </summary>
    /// <param name="system">The actor system used to attach the logger</param>
    protected void InitializeLogger(ActorSystem system)
    {
        if (Output == null) 
            return;
            
        var systemImpl = system as ActorSystemImpl ?? throw new InvalidOperationException("Expected ActorSystemImpl");
        
        // Create logger actor synchronously to avoid deadlock during parallel test execution
        // Use AttachChildWithAsync with isAsync:false to create LocalActorRef instead of RepointableActorRef
        var logger = systemImpl.Provider.SystemGuardian.Cell.AttachChildWithAsync(
            Props.Create(() => new TestOutputLogger(Output)),
            isSystemService: true,  // Mark as system service
            isAsync: false,         // Create synchronously to avoid deadlock
            name: "log-test");
        
        // Send the initialization message without waiting for response to avoid deadlock
        // The logger will subscribe to the event stream when it processes this message
        logger.Tell(new InitializeLogger(system.EventStream), ActorRefs.NoSender);
    }

    protected void InitializeLogger(ActorSystem system, string prefix)
    {
        if (Output == null) 
            return;
            
        var systemImpl = system as ActorSystemImpl ?? throw new InvalidOperationException("Expected ActorSystemImpl");
        
        // Create logger actor synchronously to avoid deadlock during parallel test execution
        var logger = systemImpl.Provider.SystemGuardian.Cell.AttachChildWithAsync(
            Props.Create(() => new TestOutputLogger(
                string.IsNullOrEmpty(prefix) ? Output : new PrefixedOutput(Output, prefix))),
            isSystemService: true,  // Mark as system service
            isAsync: false,         // Create synchronously to avoid deadlock
            name: "log-test");
        
        // Send the initialization message without waiting for response to avoid deadlock
        // The logger will subscribe to the event stream when it processes this message
        logger.Tell(new InitializeLogger(system.EventStream), ActorRefs.NoSender);
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    /// <param name="disposing">
    /// if set to <c>true</c> the method has been called directly or indirectly by a  user's code.
    /// Managed and unmanaged resources will be disposed.<br /> if set to <c>false</c> the method
    /// has been called by the runtime from inside the finalizer and only unmanaged resources can
    ///  be disposed.
    /// </param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposing || _disposed)
            return;
            
        _disposing = true;
        try
        {
            AfterAll();
        }
        finally
        {
            Shutdown();
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
    }
}