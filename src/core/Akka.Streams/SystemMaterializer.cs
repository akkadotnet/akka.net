using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Streams.Implementation;

namespace Akka.Streams
{
    // ARTERY: keeping this as internal for porting purposes, will need to change it to public later
    internal class SystemMaterializerExtension : ExtensionIdProvider<SystemMaterializer>
    {
        public override SystemMaterializer CreateExtension(ExtendedActorSystem system)
        {
            return new SystemMaterializer(system);
        }
    }

    // ARTERY: keeping this as internal for porting purposes, will need to change it to public later
    internal sealed class SystemMaterializer : IExtension
    {
        public static SystemMaterializer Get(ActorSystem system)
            => system.WithExtension<SystemMaterializer, SystemMaterializerExtension>();

        private readonly TaskCompletionSource<IMaterializer> _systemMaterializerPromise;
        private readonly ActorMaterializerSettings _materializerSettings;
        private readonly TimeSpan _materializerTimeout;
        private readonly IActorRef _materializerGuardian;

        public readonly IMaterializer Materializer;

        public SystemMaterializer(ExtendedActorSystem system)
        {
            _systemMaterializerPromise = new TaskCompletionSource<IMaterializer>();
            _materializerSettings = ActorMaterializerSettings.Create(system);
            _materializerTimeout = system.Settings.Config.GetTimeSpan("akka.stream.materializer.creation-timeout");
            _materializerGuardian = system.SystemActorOf(
                MaterializerGuardian
                    .Props(_systemMaterializerPromise, _materializerSettings)
                    .WithDispatcher(Dispatchers.InternalDispatcherId)
                    .WithDeploy(Deploy.Local),
                "Materializers");

            Materializer = _systemMaterializerPromise.Task.ConfigureAwait(false).GetAwaiter().GetResult();
        }

        internal IMaterializer CreateAdditionalSystemMaterializer()
        {
            var started = _materializerGuardian
                .Ask<MaterializerGuardian.MaterializerStarted>(
                    MaterializerGuardian.StartMaterializer.Instance, 
                    _materializerTimeout);
            
            return started.ConfigureAwait(false).GetAwaiter().GetResult().Materializer;
        }

        internal IMaterializer CreateAdditionalLegacySystemMaterializer(
            string namePrefix,
            ActorMaterializerSettings settings)
        {
            var started = _materializerGuardian
                .Ask<MaterializerGuardian.MaterializerStarted>(
                    new MaterializerGuardian.LegacyStartMaterializer(namePrefix, settings), 
                    _materializerTimeout);
            
            return started.ConfigureAwait(false).GetAwaiter().GetResult().Materializer;
        }
    }
}
