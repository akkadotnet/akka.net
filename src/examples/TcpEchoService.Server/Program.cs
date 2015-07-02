using System;
using System.Net;
using Akka.Actor;

namespace TcpEchoService.Server
{
    class Program
    {
        static void Main(string[] args)
        {
            using (var system = ActorSystem.Create("echo-server-system"))
            {
                var port = 9001;
                var actor = system.ActorOf(Props.Create(() => new EchoService(new IPEndPoint(IPAddress.Any, port))), "echo-service");

                /**
                 *  Now you should be able to connect with current TCP actor using i.e. telnet command:
                 *  $> telnet 127.0.0.1 9001
                 */

                Console.WriteLine("TCP server is listening on *:{0}", port);
                Console.WriteLine("ENTER to exit...");
                Console.ReadLine();
            }
        }
    }
}
