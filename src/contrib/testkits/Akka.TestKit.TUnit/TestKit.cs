//-----------------------------------------------------------------------
// <copyright file="TestKit.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.IO;
using System.Text;
using System.Threading;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit.TUnit.Internals;
using TUnit.Core;

namespace Akka.TestKit.TUnit;

/// <summary>
/// An Akka.NET TestKit integrated with the <a href="https://tunit.dev/">TUnit</a> test framework.
/// </summary>
[AkkaTUnitLifecycle]
public class TestKit : TestKitBase, IDisposable
{
    private sealed class PrefixedTextWriter(TextWriter output, string prefix) : TextWriter
    {
        public override Encoding Encoding => output.Encoding;

        public override void WriteLine(string? value)
            => output.WriteLine(prefix + value);
    }

    private bool _disposed;
    private bool _disposing;
    private bool _ambientContextApplied;
    private SynchronizationContext? _previousSynchronizationContext;
    private ActorCell? _previousActorCell;

    /// <summary>
    /// The writer used to capture test output.
    /// </summary>
    protected TextWriter? Output { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TestKit"/> class.
    /// </summary>
    /// <param name="system">The actor system to use for testing, or <see langword="null"/> to create one.</param>
    /// <param name="output">An optional output writer. TUnit's current test output is used when omitted.</param>
    public TestKit(ActorSystem? system = null, TextWriter? output = null)
        : base(Assertions, system)
    {
        Output = output;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TestKit"/> class.
    /// </summary>
    /// <param name="config">The setup used to configure the actor system.</param>
    /// <param name="actorSystemName">The actor system name. The default is "test".</param>
    /// <param name="output">An optional output writer. TUnit's current test output is used when omitted.</param>
    public TestKit(ActorSystemSetup config, string? actorSystemName = null, TextWriter? output = null)
        : base(Assertions, config, actorSystemName)
    {
        Output = output;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TestKit"/> class.
    /// </summary>
    /// <param name="config">The configuration used to create the actor system.</param>
    /// <param name="actorSystemName">The actor system name. The default is "test".</param>
    /// <param name="output">An optional output writer. TUnit's current test output is used when omitted.</param>
    public TestKit(Config config, string? actorSystemName = null, TextWriter? output = null)
        : base(Assertions, config, actorSystemName)
    {
        Output = output;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TestKit"/> class.
    /// </summary>
    /// <param name="config">The HOCON configuration used to create the actor system.</param>
    /// <param name="output">An optional output writer. TUnit's current test output is used when omitted.</param>
    public TestKit(string config, TextWriter? output = null)
        : base(Assertions, ConfigurationFactory.ParseString(config))
    {
        Output = output;
    }

    /// <summary>
    /// A configuration with the default TestKit logging settings.
    /// </summary>
    public new static Config DefaultConfig => TestKitBase.DefaultConfig;

    /// <summary>
    /// A configuration with all TestKit logging settings enabled.
    /// </summary>
    public new static Config FullDebugConfig => TestKitBase.FullDebugConfig;

    /// <summary>
    /// Common assertions used by the TUnit TestKit.
    /// </summary>
    protected static TUnitAssertions Assertions { get; } = new();

    internal void OnTestStart(TestContext context)
    {
        if (_ambientContextApplied)
            return;

        _previousSynchronizationContext = SynchronizationContext.Current;
        _previousActorCell = InternalCurrentActorCellKeeper.Current;

        var actorCell = this is INoImplicitSender
            ? null
            : (base.TestActor as ActorRefWithCell)?.Underlying as ActorCell;

        InternalCurrentActorCellKeeper.Current = actorCell;
        SynchronizationContext.SetSynchronizationContext(
            new ActorCellKeepingSynchronizationContext(actorCell, _previousSynchronizationContext));
        _ambientContextApplied = true;

        Output ??= context.OutputWriter;
        if (Output is not null)
            InitializeLogger(Sys);
    }

    internal void OnTestEnd()
        => RestoreAmbientContext();

    /// <summary>
    /// The actor system used for testing.
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
    /// The default test actor.
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
    /// Called when the test instance is being disposed, before its actor system is shut down.
    /// </summary>
    protected virtual void AfterAll()
    {
    }

    /// <summary>
    /// Attaches a logger that writes actor-system log events to the current test output.
    /// </summary>
    protected void InitializeLogger(ActorSystem system)
        => InitializeLogger(system, string.Empty);

    /// <summary>
    /// Attaches a logger that prefixes actor-system log events written to the current test output.
    /// </summary>
    protected void InitializeLogger(ActorSystem system, string prefix)
    {
        if (Output is null)
            return;

        var systemImpl = system as ActorSystemImpl ?? throw new InvalidOperationException("Expected ActorSystemImpl");
        var writer = string.IsNullOrEmpty(prefix) ? Output : new PrefixedTextWriter(Output, prefix);
        var logger = systemImpl.Provider.SystemGuardian.Cell.AttachChildWithAsync(
            Props.Create(() => new TestOutputLogger(writer)),
            isSystemService: true,
            isAsync: false,
            name: "log-test");
        logger.Tell(new InitializeLogger(system.EventStream), ActorRefs.NoSender);
    }

    /// <summary>
    /// Releases the TestKit and shuts down its actor system.
    /// </summary>
    /// <param name="disposing"><see langword="true"/> when called through <see cref="Dispose()"/>.</param>
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
            try
            {
                Shutdown();
                _disposed = true;
            }
            finally
            {
                RestoreAmbientContext();
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
        => Dispose(true);

    private void EnsureImplicitSender()
    {
        if (this is INoImplicitSender)
            return;

        var actorCell = (ActorCell)((ActorRefWithCell)base.TestActor).Underlying;
        if (InternalCurrentActorCellKeeper.Current is null)
            InternalCurrentActorCellKeeper.Current = actorCell;

        if (SynchronizationContext.Current is not ActorCellKeepingSynchronizationContext)
        {
            SynchronizationContext.SetSynchronizationContext(
                new ActorCellKeepingSynchronizationContext(actorCell, SynchronizationContext.Current));
        }
    }

    private void RestoreAmbientContext()
    {
        if (!_ambientContextApplied)
            return;

        InternalCurrentActorCellKeeper.Current = _previousActorCell;
        SynchronizationContext.SetSynchronizationContext(_previousSynchronizationContext);
        _ambientContextApplied = false;
    }
}
