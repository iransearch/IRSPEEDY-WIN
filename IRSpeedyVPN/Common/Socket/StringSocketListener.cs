using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;

namespace IRSpeedyVPN.Common
{
    internal class StringSocketListener
    {
        public delegate void OnDataRecieved(TcpClient client, string data);
        public event OnDataRecieved onDataReceived;

        TcpListener listener;
        List<TcpClient> clients;
        byte[] buffer = new byte[1024];
        int maxClientCount;
        public StringSocketListener(int port,int maxConnection)
        {
            maxClientCount = maxConnection;
            clients = new List<TcpClient>();
            listener = new TcpListener(new System.Net.IPAddress(new byte[] { 127, 0, 0, 1 }), port);
            
        }
        public void Listen()
        {
            listener.Start();
            listener.BeginAcceptTcpClient(OnTcpClientAccepted, listener);
        }
        private void OnTcpClientAccepted(IAsyncResult ar)
        {
            TcpClient client = null;
            try
            {
                client = listener.EndAcceptTcpClient(ar);
                clients.Add(client);
                client.GetStream().BeginRead(buffer, 0, buffer.Length, OnClientDataRecieved, client);
                if (clients.Count() < maxClientCount)
                    listener.BeginAcceptTcpClient(OnTcpClientAccepted, listener);
            }
            catch
            {
                if (client != null)
                {
                    RemoveClient(client);
                }
            }
        }
        private void OnClientDataRecieved(IAsyncResult ar)
        {
            TcpClient client = (TcpClient)ar.AsyncState;
            try
            {
                var stream = client.GetStream();
                int len = stream.EndRead(ar);
                if (len > 0)
                {
                    if (onDataReceived != null)
                    {
                        onDataReceived.Invoke(client, buffer.ToUTF8String(0, len));
                    }
                    stream.BeginRead(buffer, 0, buffer.Length, OnClientDataRecieved, client);
                }
                else
                {
                    RemoveClient(client);
                }
            }
            catch
            {
                RemoveClient(client);
            }
        }
        public void Write(string data,TcpClient client)
        {
            byte[] bdata = data.ToUTF8Bytes();
            foreach (TcpClient c in clients)
            {
                if (client == null || c.Equals(client))
                {
                    client.GetStream().Write(bdata, 0, bdata.Length);
                }
            }
        }
        void RemoveClient(TcpClient client)
        {
            if (clients.Count() == maxClientCount)
                listener.BeginAcceptTcpClient(OnTcpClientAccepted, listener);
            client.Close();
            clients.Remove(client);            
        }
    }
}