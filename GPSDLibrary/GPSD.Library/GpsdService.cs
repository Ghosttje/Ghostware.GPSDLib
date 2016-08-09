﻿using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using GPSD.Library.Models;
using Newtonsoft.Json;

namespace GPSD.Library
{     
    public class GpsdService
    {
        #region Private Properties

        private TcpClient _client;

        private readonly string _serverAddress;
        private readonly int _serverPort;

        private bool _proxyEnabled;
        private string _proxyAddress;
        private int _proxyPort;
        private bool _proxyAuthenticationEnabled;
        private string _proxyUsername;
        private string _proxyPassword;

        #endregion

        #region Properties

        public bool IsRunning { get; set; }
        
        public GpsdVersion GpsdVersion { get; set; }
        public int ReadFrequenty = 10;

        #endregion

        #region Constructors

        public GpsdService(string serverAddress, int serverPort)
        {
            _serverAddress = serverAddress;
            _serverPort = serverPort;
            IsRunning = true;
        }

        public GpsdService(string serverAddress, int serverPort, GpsOptions gpsOptions = null) : this(serverAddress, serverPort)
        {
            
        }

        #endregion

        #region Service Functionality

        public void StartService()
        {
            using (_client = GetTcpClient())
            {
                if (!_client.Connected) return;
                
                var networkStream = _client.GetStream();
                var streamReader = new StreamReader(networkStream);

                var line = streamReader.ReadLine();
                GpsdVersion = JsonConvert.DeserializeObject<GpsdVersion>(line);
                Console.WriteLine(GpsdVersion.ToString());

                var streamWriter = new StreamWriter(networkStream);
                streamWriter.WriteLine(GpsCommands.EnableCommand);
                streamWriter.Flush();

                while (IsRunning && _client.Connected)
                {
                    line = streamReader.ReadLine();
                    var classType = JsonConvert.DeserializeObject<DataClassType>(line);
                    Console.WriteLine(line);
                    Thread.Sleep(ReadFrequenty);
                }
            }
        }

        public void StopService()
        {
            IsRunning = false;

            var networkStream = _client.GetStream();
            var byteData = Encoding.ASCII.GetBytes(GpsCommands.DisableCommand);
            networkStream.Write(byteData, 0, byteData.Length);

            _client.Close();
        }

        #endregion

        #region Proxies

        public void SetProxy(string proxyAddress, int proxyPort)
        {
            _proxyEnabled = true;
            _proxyAddress = proxyAddress;
            _proxyPort = proxyPort;
        }

        public void SetProxyAuthentication(string username, string password)
        {
            _proxyAuthenticationEnabled = true;
            _proxyUsername = username;
            _proxyPassword = password;
        }

        public void DisableProxy()
        {
            _proxyEnabled = false;
        }

        private TcpClient GetTcpClient()
        {
            return _proxyEnabled ? ConnectViaHttpProxy() : new TcpClient(_serverAddress, _serverPort);
        }

        private TcpClient ConnectViaHttpProxy()
        {
            var proxy = WebRequest.GetSystemWebProxy();

            var uriBuilder = new UriBuilder
            {
                Scheme = Uri.UriSchemeHttp,
                Host = _proxyAddress,
                Port = _proxyPort
            };

            var proxyUri = uriBuilder.Uri;
            var request = WebRequest.Create("http://" + _serverAddress + ":" + _serverPort);
            var webProxy = new WebProxy(proxyUri);

            request.Proxy = webProxy;
            request.Method = "CONNECT";

            if (_proxyAuthenticationEnabled)
            {
                webProxy.Credentials = new NetworkCredential(_proxyUsername, _proxyPassword);
            }
            else
            {
                webProxy.UseDefaultCredentials = true;
            }

            var response = request.GetResponse();
            var responseStream = response.GetResponseStream();
            Debug.Assert(responseStream != null);

            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;

            var rsType = responseStream.GetType();
            var connectionProperty = rsType.GetProperty("Connection", flags);

            var connection = connectionProperty.GetValue(responseStream, null);
            var connectionType = connection.GetType();
            var networkStreamProperty = connectionType.GetProperty("NetworkStream", flags);

            var networkStream = networkStreamProperty.GetValue(connection, null);
            var nsType = networkStream.GetType();
            var socketProperty = nsType.GetProperty("Socket", flags);
            var socket = (Socket)socketProperty.GetValue(networkStream, null);

            return new TcpClient { Client = socket };
        }

        #endregion
    }
}
