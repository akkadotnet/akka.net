//-----------------------------------------------------------------------
// <copyright file="FailedSource.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Stage;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// A source that immediately fails with the provided exception.
    /// This is the GraphStage equivalent of ErrorPublisher.
    /// </summary>
    /// <typeparam name="T">The type of elements this source would emit (none will be emitted)</typeparam>
    internal sealed class FailedSource<T> : GraphStage<SourceShape<T>>
    {
        private readonly Exception _failure;

        public FailedSource(Exception failure, string name)
        {
            _failure = failure ?? throw new ArgumentNullException(nameof(failure));
            Out = new Outlet<T>($"{name}.out");
            Shape = new SourceShape<T>(Out);
        }

        public Outlet<T> Out { get; }
        public override SourceShape<T> Shape { get; }

        protected override Attributes InitialAttributes => DefaultAttributes.FailedSource;

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new Logic(this);
        }

        public override string ToString() => "FailedSource";

        private sealed class Logic : OutGraphStageLogic
        {
            private readonly FailedSource<T> _source;

            public Logic(FailedSource<T> source) : base(source.Shape)
            {
                _source = source;
                SetHandler(source.Out, this);
            }

            public override void PreStart()
            {
                // Fail the stage during the PreStart lifecycle hook
                // This ensures the failure happens after the GraphInterpreter
                // is properly initialized and ActiveStage is set
                FailStage(_source._failure);
            }

            public override void OnPull()
            {
                // This should never be called since we fail in PreStart
                // But we need to implement it as part of the OutHandler contract
            }
        }
    }
}