using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Akka.Actor;

namespace WindowsForms.Sample
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        /// <inheritdoc />
        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            // This is execured in UI thread
            var system = ActorSystem.Create("WindowsFormSystem");
            var helloActor = system.ActorOf(Props.Create<HelloActor>());
            var response = await helloActor.Ask<string>("Hello");
            MessageBox.Show($"Actor response: {response}");
        }

        public class HelloActor : ReceiveActor
        {
            public HelloActor()
            {
                Receive<string>(msg => Sender.Tell("Hello"));
            }
        }
    }
}