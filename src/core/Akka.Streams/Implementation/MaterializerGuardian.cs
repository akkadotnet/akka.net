using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Util;

namespace Akka.Streams.Implementation
{
    internal class MaterializerGuardian : ReceiveActor
    {
        #region Static region

        public sealed class StartMaterializer
        {
            public static StartMaterializer Instance = new StartMaterializer();
            private StartMaterializer(){}
        }

        public sealed class MaterializerStarted
        {
            public IMaterializer Materializer { get; }

            public MaterializerStarted(IMaterializer materializer)
            {
                Materializer = materializer;
            }
        }

        // this is available to keep backwards compatibility with ActorMaterializer and should
        // be removed together with ActorMaterializer in the future
        public sealed class LegacyStartMaterializer
        {
            public string NamePrefix { get; }
            public ActorMaterializerSettings Settings { get; }

            public LegacyStartMaterializer(string namePrefix, ActorMaterializerSettings settings)
            {
                NamePrefix = namePrefix;
                Settings = settings;
            }
        }

        public static Props Props(TaskCompletionSource<IMaterializer> systemMaterializer, ActorMaterializerSettings materializerSettings)
            => Actor.Props.Create<MaterializerGuardian>(systemMaterializer, materializerSettings);

        #endregion

        private readonly TaskCompletionSource<IMaterializer> _systemMaterializerPromise;
        private readonly ActorMaterializerSettings _materializerSettings;

        private readonly Attributes _defaultAttributes;
        private readonly string _defaultNamePrefix = "flow";

        private readonly IMaterializer _systemMaterializer; 

        public MaterializerGuardian(
            TaskCompletionSource<IMaterializer> systemMaterializerPromise, 
            ActorMaterializerSettings materializerSettings)
        {
            _systemMaterializerPromise = systemMaterializerPromise;
            _materializerSettings = materializerSettings;

            _defaultAttributes = materializerSettings.ToAttributes();

            _systemMaterializer = InternalStartMaterializer(_defaultNamePrefix, Option<ActorMaterializerSettings>.None);
            _systemMaterializerPromise.SetResult(_systemMaterializer);

            Receive<StartMaterializer>(msg =>
                Sender.Tell(new MaterializerStarted(InternalStartMaterializer(
                    _defaultNamePrefix, Option<ActorMaterializerSettings>.None))));
            Receive<LegacyStartMaterializer>(msg =>
                Sender.Tell(new MaterializerStarted(InternalStartMaterializer(msg.NamePrefix, msg.Settings))));
        }

        private IMaterializer InternalStartMaterializer(string namePrefix, Option<ActorMaterializerSettings> settings)
        {
            // ARTERY: PhasedFusingActorMaterializer isn't implemented yet
            throw new NotImplementedException();

            /*
            var attributes = settings.HasValue ? settings.Value.ToAttributes() : _defaultAttributes;

            return new PhasedFusingActorMaterializer(
                Context, 
                namePrefix, 
                settings.GetOrElse(_materializerSettings),
                attributes);
            */
        }
    }
}
