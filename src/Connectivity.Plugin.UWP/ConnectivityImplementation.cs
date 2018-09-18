using System;
using System.Collections.Generic;
using System.Linq;
using Plugin.Connectivity.Abstractions;
using Windows.Networking.Connectivity;
using System.Threading.Tasks;
using Windows.Networking.Sockets;
using Windows.Networking;
using System.Diagnostics;
using Windows.ApplicationModel.Core;
using System.Threading;

namespace Plugin.Connectivity
{
    /// <summary>
    /// Connectivity Implementation for WinRT
    /// </summary>
    public class ConnectivityImplementation : BaseConnectivity
    {
        bool isConnected;
        /// <summary>
        /// Default constructor
        /// </summary>
        public ConnectivityImplementation()
        {
            isConnected = IsConnected;
            NetworkInformation.NetworkStatusChanged += NetworkStatusChanged;
        }

        async void NetworkStatusChanged(object sender)
        {
            var previous = isConnected;
            var newConnected = IsConnected;

            var dispatcher = CoreApplication.MainView.CoreWindow?.Dispatcher;

            if (dispatcher != null)
            {
                await dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    if (previous != newConnected)
                        OnConnectivityChanged(new ConnectivityChangedEventArgs { IsConnected = newConnected });

                    OnConnectivityTypeChanged(new ConnectivityTypeChangedEventArgs { IsConnected = newConnected, ConnectionTypes = this.ConnectionTypes });
                });
            }
            else
            {
                if (previous != newConnected)
                    OnConnectivityChanged(new ConnectivityChangedEventArgs { IsConnected = newConnected });

                OnConnectivityTypeChanged(new ConnectivityTypeChangedEventArgs { IsConnected = newConnected, ConnectionTypes = this.ConnectionTypes });
            }
        }

        /// <summary>
        /// Gets if there is an active internet connection
        /// </summary>
        public override bool IsConnected
        {
            get
            {
                var profile = NetworkInformation.GetInternetConnectionProfile();
                if (profile == null)
                    isConnected = false;
                else
                {
                    var level = profile.GetNetworkConnectivityLevel();
                    isConnected = level != NetworkConnectivityLevel.None && level != NetworkConnectivityLevel.LocalAccess;
                }

                return isConnected;
            }
        }


        /// <summary>
        /// Checks if remote is reachable. RT apps cannot do loopback so this will alway return false.
        /// You can use it to check remote calls though.
        /// </summary>
        /// <param name="host"></param>
        /// <param name="msTimeout"></param>
        /// <returns></returns>
        public override async Task<bool> IsReachable(string host, int msTimeout = 5000)
        {
            if (string.IsNullOrEmpty(host))
                throw new ArgumentNullException("host");

            if (!IsConnected)
                return false;

            try
            {
                var serverHost = new HostName(host);
                using (var client = new StreamSocket())
                {
                    var cancellationTokenSource = new CancellationTokenSource();
                    cancellationTokenSource.CancelAfter(msTimeout);

                    await client.ConnectAsync(serverHost, "http").AsTask(cancellationTokenSource.Token);
                    return true;
                }


            }
            catch (Exception ex)
            {
                Debug.WriteLine("Unable to reach: " + host + " Error: " + ex);
                return false;
            }
        }

        /// <summary>
        /// Tests if a remote host name is reachable 
        /// </summary>
        /// <param name="host">Host name can be a remote IP or URL of website</param>
        /// <param name="port">Port to attempt to check is reachable.</param>
        /// <param name="msTimeout">Timeout in milliseconds.</param>
        /// <returns></returns>
        public override async Task<bool> IsRemoteReachable(string host, int port = 80, int msTimeout = 5000)
        {
            if (string.IsNullOrEmpty(host))
                throw new ArgumentNullException("host");

            if (!IsConnected)
                return false;

            host = host.Replace("http://www.", string.Empty).
              Replace("http://", string.Empty).
              Replace("https://www.", string.Empty).
              Replace("https://", string.Empty).
              TrimEnd('/');

            try
            {
                using (var tcpClient = new StreamSocket())
                {
                    var cancellationTokenSource = new CancellationTokenSource();
                    cancellationTokenSource.CancelAfter(msTimeout);

                    await tcpClient.ConnectAsync(
                        new Windows.Networking.HostName(host),
                        port.ToString(),
                        SocketProtectionLevel.PlainSocket).AsTask(cancellationTokenSource.Token);

                    var localIp = tcpClient.Information.LocalAddress.DisplayName;
                    var remoteIp = tcpClient.Information.RemoteAddress.DisplayName;

                    tcpClient.Dispose();

                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Unable to reach: " + host + " Error: " + ex);
                return false;
            }
        }

        /// <summary>
        /// Gets the list of all active connection types.
        /// </summary>
        public override IEnumerable<ConnectionType> ConnectionTypes
        {
            get
            {
                var networkInterfaceList = NetworkInformation.GetConnectionProfiles();
                foreach (var networkInterfaceInfo in networkInterfaceList.Where(networkInterfaceInfo => networkInterfaceInfo.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.None))
                {
                    var type = ConnectionType.Other;

                    if (networkInterfaceInfo.NetworkAdapter != null)
                    {

                        switch (networkInterfaceInfo.NetworkAdapter.IanaInterfaceType)
                        {
                            case 6:
                                type = ConnectionType.Desktop;
                                break;
                            case 71:
                                type = ConnectionType.WiFi;
                                break;
                            case 243:
                            case 244:
                                type = ConnectionType.Cellular;
                                break;
                        }
                    }

                    yield return type;
                }
            }
        }

        /// <summary>
        /// Retrieves a list of available bandwidths for the platform.
        /// Only active connections.
        /// </summary>
        public override IEnumerable<UInt64> Bandwidths
        {
            get
            {
                var networkInterfaceList = NetworkInformation.GetConnectionProfiles();
                foreach (var networkInterfaceInfo in networkInterfaceList.Where(networkInterfaceInfo => networkInterfaceInfo.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.None))
                {
                    UInt64 speed = 0;

                    if (networkInterfaceInfo.NetworkAdapter != null)
                    {
                        speed = (UInt64)networkInterfaceInfo.NetworkAdapter.OutboundMaxBitsPerSecond;
                    }

                    yield return speed;
                }
            }
        }
        private bool disposed = false;
        /// <summary>
        /// Dispose
        /// </summary>
        /// <param name="disposing"></param>
        public override void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    NetworkInformation.NetworkStatusChanged -= NetworkStatusChanged;
                }

                disposed = true;
            }

            base.Dispose(disposing);
        }
    }
}
