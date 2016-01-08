using System;
using Akka.Actor;
using Android.App;
using Android.Content;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.OS;
using Android.Util;

namespace Akka.Android.Demo
{
    [Activity(Label = "Akka.Android.Demo", MainLauncher = true, Icon = "@drawable/icon")]
    public class MainActivity : Activity
    {
        protected override void OnCreate(Bundle bundle)
        {
            base.OnCreate(bundle);
            SetContentView(Resource.Layout.Main);

            var system = ActorSystem.Create("MySystem");
            var bla = system.ActorOf<HelloActor>();

            system.Scheduler.ScheduleTellRepeatedly(TimeSpan.Zero, TimeSpan.FromSeconds(10), bla, 1, ActorRefs.NoSender);
        }
    }

    public class HelloActor : ReceiveActor
    {
        public HelloActor()
        {
            Receive<int>(_ =>
            {
                Log.Debug("tst", null, "hello");
            });
        }
    }
}

