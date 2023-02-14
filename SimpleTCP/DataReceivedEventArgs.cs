using System;

namespace SimpleTCP
{
    public class DataReceivedEventArgs : EventArgs
    {
        public byte[] data;
        public string ip;

        public DataReceivedEventArgs(byte[] data, string ip)
        {
            this.data = data;
            this.ip = ip;
        }
    }
}
