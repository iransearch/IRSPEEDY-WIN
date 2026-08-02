using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;

namespace IRSpeedyVPN.Common.Socket
{
    public class StringSocketClient:IDisposable
    {
        TcpClient client;
        public delegate void OnDataRecieved(string data);
        public event OnDataRecieved onDataReceived;
        NetworkStream stream;
        byte[] buffer = new byte[1024];
        public StringSocketClient()
        {
            client = new TcpClient();
        }
        public void Connect(string ip, int port)
        {
            client.Connect(ip, port);
            stream =client.GetStream();
            stream.BeginRead(buffer, 0, buffer.Length, OnClientDataRecieved, stream);
        }
        private void OnClientDataRecieved(IAsyncResult ar)
        {
    
            try
            {

                int len = stream.EndRead(ar);
                if (len > 0)
                {
                    if (onDataReceived != null)
                    {
                        onDataReceived.Invoke( buffer.ToUTF8String(0, len));
                    }
                    stream.BeginRead(buffer, 0, buffer.Length, OnClientDataRecieved, client);
                }
                else
                {
                    Dispose();
                }
            }
            catch
            { 
                Dispose();
            }
        }
        public void Write(string data)
        {
            byte[] bdata = data.ToUTF8Bytes();           
            client.GetStream().Write(bdata, 0, bdata.Length);
             
        }

        public void Dispose()
        {
            if (stream != null)
                stream.Close();
            if (client != null)
                client.Close();
        }
    }
}
