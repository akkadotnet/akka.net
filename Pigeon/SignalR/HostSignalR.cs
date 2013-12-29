using Microsoft.AspNet.SignalR;
using Microsoft.Owin.Cors;
using Microsoft.Owin.Host.HttpListener;
using Microsoft.Owin.Hosting;
using Owin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.SignalR
{
    public class PigeonHostSignalR : IDisposable
    {
        private IDisposable app;

        public static PigeonHostSignalR Start(string systemname,string url)
        {
            var t = typeof(OwinHttpListener);

            var host = new PigeonHostSignalR();
            host.app = WebApp.Start<Startup>(new StartOptions
            {
                Urls = { url },
                ServerFactory = "Microsoft.Owin.Host.HttpListener",
            });
            return host;
        }

        public void Dispose()
        {
            app.Dispose();
        }
    }
    class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            app.UseCors(CorsOptions.AllowAll);
            app.MapSignalR();
        }
    }
    public class MyHub : Hub
    {
        public void Ping()
        {
            Clients.Caller.Pong();
        }
    }
}
