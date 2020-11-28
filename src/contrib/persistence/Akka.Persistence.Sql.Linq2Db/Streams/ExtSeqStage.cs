using System;
using System.Threading.Tasks;
using Akka.Streams;
using Akka.Streams.Stage;
using LanguageExt;

namespace Akka.Persistence.Sql.Linq2Db.Streams
{
    public sealed class ExtSeqStage<T> : GraphStageWithMaterializedValue<SinkShape<T>, Task<Seq<T>>>
    {
        #region stage logic

        private sealed class Logic : InGraphStageLogic
        {
            private readonly ExtSeqStage<T> _stage;
            private readonly TaskCompletionSource<Seq<T>> _promise;
            private Seq<T> _buf = Seq<T>.Empty;
            private bool _completionSignalled;

            public Logic(ExtSeqStage<T> stage, TaskCompletionSource<Seq<T>> promise) : base(stage.Shape)
            {
                _stage = stage;
                _promise = promise;

                SetHandler(stage.In, this);
            }

            public override void OnPush()
            {
                _buf = _buf.Add(Grab(_stage.In));
                Pull(_stage.In);
            }

            public override void OnUpstreamFinish()
            {
                _promise.TrySetResult(_buf);
                _completionSignalled = true;
                CompleteStage();
            }

            public override void OnUpstreamFailure(Exception e)
            {
                _promise.TrySetException(e);
                _completionSignalled = true;
                FailStage(e);
            }

            public override void PostStop()
            {
                if (!_completionSignalled)
                    _promise.TrySetException(new AbruptStageTerminationException(this));
            }

            public override void PreStart() => Pull(_stage.In);
        }

        #endregion

        /// <summary>
        /// TBD
        /// </summary>
        public ExtSeqStage()
        {
            Shape = new SinkShape<T>(In);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = SinkAttr;
        public static readonly Attributes SinkAttr = Attributes.CreateName("languageExtSeqSink");
        /// <summary>
        /// TBD
        /// </summary>
        public override SinkShape<T> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T> In = new Inlet<T>("Seq.in");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        public override ILogicAndMaterializedValue<Task<Seq<T>>> CreateLogicAndMaterializedValue(
            Attributes inheritedAttributes)
        {
            var promise = new TaskCompletionSource<Seq<T>>();
            return new LogicAndMaterializedValue<Task<Seq<T>>>(new Logic(this, promise), promise.Task);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "LanguageExtSeqStage";
    }
}