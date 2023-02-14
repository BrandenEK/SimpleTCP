using System;

namespace SimpleTCP
{
    public class ClientConnectionEventArgs : EventArgs
    {
        public string ip;

        public ClientConnectionEventArgs(string ip)
        {
            this.ip = ip;
        }
    }
}
