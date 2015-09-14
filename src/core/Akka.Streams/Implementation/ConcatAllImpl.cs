using System;
using Akka.Actor;
using Akka.Streams.Dsl;

namespace Akka.Streams.Implementation
{
    public class ConcatAllImpl : MultiStreamInputProcessor
    {
        public static Props Props(ActorMaterializer materializer)
        {
            return Actor.Props.Create(() => new ConcatAllImpl(materializer)).WithDeploy(Deploy.Local);
        }

        protected readonly ActorMaterializer Materializer;
        protected readonly TransferPhase TakeNextSubstream;

        public ConcatAllImpl(ActorMaterializer materializer) : base(materializer.Settings)
        {
            Materializer = materializer;
            TakeNextSubstream = new TransferPhase(PrimaryInputs.NeedsInput.And(PrimaryOutputs.NeedsDemand), () =>
            {
                var source = (Source<object, object>)PrimaryInputs.DequeueInputElement();
                var publisher = source.RunWith(Sink.Publisher, Materializer);
                // FIXME we can pass the flow to createSubstreamInput (but avoiding copy impl now)
                var inputs = CreateAndSubscribeSubstreamInput(publisher);
                NextPhase(StreamSubstream(inputs));
            });

            InitialPhase(1, TakeNextSubstream);
        }

        protected TransferPhase StreamSubstream(SubstreamInput substream)
        {
            throw new NotImplementedException();
        }
    }
}