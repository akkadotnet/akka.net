//-----------------------------------------------------------------------
// <copyright file="IotApp.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Tutorials.Tutorial1
{
    #region iot-app
    public class IotApp
    {
        public static void Init()
        {
            using (var system = ActorSystem.Create("iot-system"))
            {
                // Create top level supervisor
                var supervisor = system.ActorOf(IotSupervisor.Props(), "iot-supervisor");
                // Exit the system after ENTER is pressed
                Console.ReadLine();
            }
        }
    }
    #endregion
}
