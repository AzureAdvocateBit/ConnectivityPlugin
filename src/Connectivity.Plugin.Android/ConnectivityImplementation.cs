using Plugin.Connectivity.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Android.Content;
using Android.Net;
using Android.Net.Wifi;
using Android.App;
using Java.Net;


namespace Plugin.Connectivity
{
    /// <summary>
    /// Implementation for Feature
    /// </summary>
    [Android.Runtime.Preserve(AllMembers = true)]
    public class ConnectivityImplementation : BaseConnectivity
    {
        private ConnectivityChangeBroadcastReceiver receiver;
        /// <summary>
        /// Default constructor
        /// </summary>
        public ConnectivityImplementation()
        {
            ConnectivityChangeBroadcastReceiver.ConnectionChanged = OnConnectivityChanged;
            ConnectivityChangeBroadcastReceiver.ConnectionTypeChanged = OnConnectivityTypeChanged;
            receiver = new ConnectivityChangeBroadcastReceiver();
            Application.Context.RegisterReceiver(receiver, new IntentFilter(ConnectivityManager.ConnectivityAction));
        }
        private ConnectivityManager connectivityManager;
        private WifiManager wifiManager;

        ConnectivityManager ConnectivityManager
        {
            get
            {
                if (connectivityManager == null || connectivityManager.Handle == IntPtr.Zero)
                    connectivityManager = (ConnectivityManager)(Application.Context.GetSystemService(Context.ConnectivityService));

                return connectivityManager;
            }
        }

        WifiManager WifiManager
        {
            get
            {
                if(wifiManager == null || wifiManager.Handle == IntPtr.Zero)
                    wifiManager = (WifiManager)(Application.Context.GetSystemService(Context.WifiService));

                return wifiManager;
            }
        }

        public static bool GetIsConnected(ConnectivityManager manager)
        {
            try
            {

                //When on API 21+ need to use getAllNetworks, else fall base to GetAllNetworkInfo
                //https://developer.android.com/reference/android/net/ConnectivityManager.html#getAllNetworks()
                if ((int)Android.OS.Build.VERSION.SdkInt >= 21)
                {
                    foreach (var network in manager.GetAllNetworks())
                    {
                        try
                        {
							var capabilities = manager.GetNetworkCapabilities(network);

							if (capabilities == null)
								continue;

							//check to see if it has the internet capability
							if (!capabilities.HasCapability(NetCapability.Internet))
								continue;

							//if on 23+ then we can also check validated
							//Indicates that connectivity on this network was successfully validated.
							//this means that you can be connected to wifi and has internet
							//2/7/18: We are removing this because apparently devices aren't reporting back the correct information :(
							//if ((int)Android.OS.Build.VERSION.SdkInt >= 23 && !capabilities.HasCapability(NetCapability.Validated))
							//	continue;

							var info = manager.GetNetworkInfo(network);

                            if (info == null || !info.IsAvailable)
                                continue;

                            if (info.IsConnected)
                                return true;
                        }
                        catch
                        {
                            //there is a possibility, but don't worry
                        }
                    }
                }
                else
                {
#pragma warning disable CS0618 // Type or member is obsolete
					foreach (var info in manager.GetAllNetworkInfo())
#pragma warning restore CS0618 // Type or member is obsolete
					{
                        if (info == null || !info.IsAvailable)
                            continue;
                        
                        if (info.IsConnected)
                            return true;
                    }
                }

                return false;
            }
            catch (Exception e)
            {
                Debug.WriteLine("Unable to get connected state - do you have ACCESS_NETWORK_STATE permission? - error: {0}", e);
                return false;
            }
        }

        /// <summary>
        /// Gets if there is an active internet connection
        /// </summary>
        public override bool IsConnected => GetIsConnected(ConnectivityManager);


        /// <summary>
        /// Tests if a host name is pingable
        /// </summary>
        /// <param name="host">The host name can either be a machine name, such as "java.sun.com", or a textual representation of its IP address (127.0.0.1)</param>
        /// <param name="msTimeout">Timeout in milliseconds</param>
        /// <returns></returns>
        public override async Task<bool> IsReachable(string host, int msTimeout = 5000)
        {
            if (string.IsNullOrEmpty(host))
                throw new ArgumentNullException(nameof(host));

            if (!IsConnected)
                return false;

            return await Task.Run(() =>
            {
                bool reachable;
                try
                {
                    reachable = InetAddress.GetByName(host).IsReachable(msTimeout);
                }
                catch (UnknownHostException ex)
                {
                    Debug.WriteLine("Unable to reach: " + host + " Error: " + ex);
                    reachable = false;
                }
                catch (Exception ex2)
                {
                    Debug.WriteLine("Unable to reach: " + host + " Error: " + ex2);
                    reachable = false;
                }
                return reachable;
            });

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
                throw new ArgumentNullException(nameof(host));

            if (!IsConnected)
                return false;

            host = host.Replace("http://www.", string.Empty).
              Replace("http://", string.Empty).
              Replace("https://www.", string.Empty).
              Replace("https://", string.Empty).
              TrimEnd('/');

            return await Task.Run(async () =>
            {
                try
                {
                    var tcs = new TaskCompletionSource<InetSocketAddress>();
                    new System.Threading.Thread(() =>
                    {
                        /* this line can take minutes when on wifi with poor or none internet connectivity
                        and Task.Delay solves it only if this is running on new thread (Task.Run does not help) */
                        InetSocketAddress result = new InetSocketAddress(host, port);

                        if (!tcs.Task.IsCompleted)
                            tcs.TrySetResult(result);

                    }).Start();

                    Task.Run(async () =>
                    {
                        await Task.Delay(msTimeout);

                        if (!tcs.Task.IsCompleted)
                            tcs.TrySetResult(null);
                    });

                    var sockaddr = await tcs.Task;

                    if (sockaddr == null)
                        return false;

                    using (var sock = new Socket())
                    {

                        await sock.ConnectAsync(sockaddr, msTimeout);
                        return true;

                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Unable to reach: " + host + " Error: " + ex);
                    return false;
                }
            });
        }

        /// <summary>
        /// Gets the list of all active connection types.
        /// </summary>
        public override IEnumerable<ConnectionType> ConnectionTypes
        {
            get
            {
                return GetConnectionTypes(ConnectivityManager);
            }
        }

        public static IEnumerable<ConnectionType> GetConnectionTypes(ConnectivityManager manager)
        {
            //When on API 21+ need to use getAllNetworks, else fall base to GetAllNetworkInfo
            //https://developer.android.com/reference/android/net/ConnectivityManager.html#getAllNetworks()
            if ((int)Android.OS.Build.VERSION.SdkInt >= 21)
            {
                foreach (var network in manager.GetAllNetworks())
                {
                    NetworkInfo info = null;
                    try
                    {
                        info = manager.GetNetworkInfo(network);
                    }
                    catch
                    {
                        //there is a possibility, but don't worry about it
                    }

                    if (info == null || !info.IsAvailable)
                        continue;

                    yield return GetConnectionType(info.Type, info.TypeName);
                }
            }
            else
            {
                foreach (var info in manager.GetAllNetworkInfo())
                {
                    if (info == null || !info.IsAvailable)
                        continue;

                    yield return GetConnectionType(info.Type, info.TypeName);
                }
            }

        }

        public static ConnectionType GetConnectionType(ConnectivityType connectivityType, string typeName)
        {

            switch (connectivityType)
            {
                case ConnectivityType.Ethernet:
                    return ConnectionType.Desktop;
                case ConnectivityType.Wimax:
                    return ConnectionType.Wimax;
                case ConnectivityType.Wifi:
                    return ConnectionType.WiFi;
                case ConnectivityType.Bluetooth:
                    return ConnectionType.Bluetooth;
                case ConnectivityType.Mobile:
                case ConnectivityType.MobileDun:
                case ConnectivityType.MobileHipri:
                case ConnectivityType.MobileMms:
                    return ConnectionType.Cellular;
                case ConnectivityType.Dummy:
                    return ConnectionType.Other;
                default:
					if (string.IsNullOrWhiteSpace(typeName))
						return ConnectionType.Other;

					var typeNameLower = typeName.ToLowerInvariant();
					if (typeNameLower.Contains("mobile"))
						return ConnectionType.Cellular;

					if (typeNameLower.Contains("wifi"))
						return ConnectionType.WiFi;


					if (typeNameLower.Contains("wimax"))
						return ConnectionType.Wimax;


					if (typeNameLower.Contains("ethernet"))
						return ConnectionType.Desktop;


					if (typeNameLower.Contains("bluetooth"))
						return ConnectionType.Bluetooth;

					return ConnectionType.Other;
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
                try
                {
                    if (ConnectionTypes.Contains(ConnectionType.WiFi))
                        return new[] { (UInt64)WifiManager.ConnectionInfo.LinkSpeed * 1000000 };
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Unable to get connected state - do you have ACCESS_WIFI_STATE permission? - error: {0}", e);
                }

                return new UInt64[] { };
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
                    if (receiver != null)
                        Application.Context.UnregisterReceiver(receiver);

                    ConnectivityChangeBroadcastReceiver.ConnectionChanged = null;
                    if (wifiManager != null)
                    {
                        wifiManager.Dispose();
                        wifiManager = null;
                    }

                    if (connectivityManager != null)
                    {
                        connectivityManager.Dispose();
                        connectivityManager = null;
                    }
                }

                disposed = true;
            }

            base.Dispose(disposing);
        }


    }
}
