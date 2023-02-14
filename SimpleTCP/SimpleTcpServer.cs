using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;

namespace SimpleTCP
{
    public class SimpleTcpServer
    {
        public SimpleTcpServer()
        {
            Delimiter = 0x13;
            StringEncoder = System.Text.Encoding.UTF8;
        }

        private List<Server.ServerListener> _listeners = new List<Server.ServerListener>();
        public byte Delimiter { get; set; }
        public System.Text.Encoding StringEncoder { get; set; }
        public bool AutoTrimStrings { get; set; }

        public event EventHandler<ClientConnectionEventArgs> ClientConnected;
        public event EventHandler<ClientConnectionEventArgs> ClientDisconnected;
        public event EventHandler<DataReceivedEventArgs> DataReceived;

        public IEnumerable<IPAddress> GetIPAddresses()
        {
            List<IPAddress> ipAddresses = new List<IPAddress>();

			IEnumerable<NetworkInterface> enabledNetInterfaces = NetworkInterface.GetAllNetworkInterfaces()
				.Where(nic => nic.OperationalStatus == OperationalStatus.Up);
			foreach (NetworkInterface netInterface in enabledNetInterfaces)
            {
                IPInterfaceProperties ipProps = netInterface.GetIPProperties();
                foreach (UnicastIPAddressInformation addr in ipProps.UnicastAddresses)
                {
                    if (!ipAddresses.Contains(addr.Address))
                    {
                        ipAddresses.Add(addr.Address);
                    }
                }
            }

            var ipSorted = ipAddresses.OrderByDescending(ip => RankIpAddress(ip)).ToList();
            return ipSorted;
        }

        public List<IPAddress> GetListeningIPs()
        {
            List<IPAddress> listenIps = new List<IPAddress>();
            foreach (var l in _listeners)
            {
                if (!listenIps.Contains(l.IPAddress))
                {
                    listenIps.Add(l.IPAddress);
                }
            }

            return listenIps.OrderByDescending(ip => RankIpAddress(ip)).ToList();
        }

        public bool DisableDelay()
        {
            bool disabled = true;
            for (int i = 0; i < _listeners.Count; i++)
            {
                disabled = _listeners[i].disableDelay() && disabled;
            }
            return disabled;
        }

        public void Send(string ipAddress, byte[] data)
        {
            for (int i = 0; i <_listeners.Count; i++)
            {
                foreach (var client in _listeners[i].ConnectedClients)
                {
                    if (client.Client.LocalEndPoint.ToString() == ipAddress)
                    {
                        client.GetStream().Write(data, 0, data.Length);
                    }
                }
            }
        }

        private int RankIpAddress(IPAddress addr)
        {
            int rankScore = 1000;

            if (IPAddress.IsLoopback(addr))
            {
                // rank loopback below others, even though their routing metrics may be better
                rankScore = 300;
            }
            else if (addr.AddressFamily == AddressFamily.InterNetwork)
            {
                rankScore += 100;
                // except...
                if (addr.GetAddressBytes().Take(2).SequenceEqual(new byte[] { 169, 254 }))
                {
                    // APIPA generated address - no router or DHCP server - to the bottom of the pile
                    rankScore = 0;
                }
            }

            if (rankScore > 500)
            {
                foreach (var nic in TryGetCurrentNetworkInterfaces())
                {
                    var ipProps = nic.GetIPProperties();
                    if (ipProps.GatewayAddresses.Any())
                    {
                        if (ipProps.UnicastAddresses.Any(u => u.Address.Equals(addr)))
                        {
                            // if the preferred NIC has multiple addresses, boost all equally
                            // (justifies not bothering to differentiate... IOW YAGNI)
                            rankScore += 1000;
                        }

                        // only considering the first NIC that is UP and has a gateway defined
                        break;
                    }
                }
            }

            return rankScore;
        }

        private static IEnumerable<NetworkInterface> TryGetCurrentNetworkInterfaces()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces().Where(ni => ni.OperationalStatus == OperationalStatus.Up);
            }
            catch (NetworkInformationException)
            {
                return Enumerable.Empty<NetworkInterface>();
            }
        }

        public SimpleTcpServer Start(int port, bool ignoreNicsWithOccupiedPorts = true)
        {
            var ipSorted = GetIPAddresses();
			bool anyNicFailed = false;
            foreach (var ipAddr in ipSorted)
            {
				try
				{
					Start(ipAddr, port);
				}
				catch (SocketException ex)
				{
					anyNicFailed = true;
				}
            }

			if (!IsStarted)
				throw new InvalidOperationException("Port was already occupied for all network interfaces");

			if (anyNicFailed && !ignoreNicsWithOccupiedPorts)
			{
				Stop();
				throw new InvalidOperationException("Port was already occupied for one or more network interfaces.");
			}

            return this;
        }

        public SimpleTcpServer Start(int port, AddressFamily addressFamilyFilter)
        {
            var ipSorted = GetIPAddresses().Where(ip => ip.AddressFamily == addressFamilyFilter);
            foreach (var ipAddr in ipSorted)
            {
                try
                {
                    Start(ipAddr, port);
                }
                catch { }
            }

            return this;
        }

		public bool IsStarted { get { return _listeners.Any(l => l.Listener.Active); } }

		public SimpleTcpServer Start(IPAddress ipAddress, int port)
        {
            Server.ServerListener listener = new Server.ServerListener(this, ipAddress, port);
            _listeners.Add(listener);

            return this;
        }

        public void Stop()
        {
			_listeners.All(l => l.QueueStop = true);
			while (_listeners.Any(l => l.Listener.Active)){
				Thread.Sleep(100);
			};
            _listeners.Clear();
        }

        public int ConnectedClientsCount
        {
            get {
                return _listeners.Sum(l => l.ConnectedClientsCount);
            }
        }

        internal void NotifyEndTransmissionRx(Server.ServerListener listener, TcpClient client, byte[] msg)
        {
            if (DataReceived != null)
            {
                DataReceived(this, new DataReceivedEventArgs(msg, client.Client.LocalEndPoint.ToString()));
            }
        }

        internal void NotifyClientConnected(Server.ServerListener listener, TcpClient newClient)
        {
            if (ClientConnected != null)
            {
                ClientConnected(this, new ClientConnectionEventArgs(newClient.Client.LocalEndPoint.ToString()));
            }
        }

        internal void NotifyClientDisconnected(Server.ServerListener listener, TcpClient disconnectedClient)
        {
            if (ClientDisconnected != null)
            {
                ClientDisconnected(this, new ClientConnectionEventArgs(disconnectedClient.Client.LocalEndPoint.ToString()));
            }
        }
	}
}
