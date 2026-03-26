using System;
using System.Diagnostics;

namespace Akka.MultiNode.RemoteHost
{
    public class RemoteHostOptions
    {
        internal RemoteHostOptions(ProcessStartInfo psi)
        {
            StartInfo = psi;
        }

        public ProcessStartInfo StartInfo { get; }

        public Action<Process> OnExit { get; set; }
        
        public DataReceivedEventHandler OutputDataReceived { get; set; }
        
        public DataReceivedEventHandler ErrorDataReceived { get; set; }
    }
}