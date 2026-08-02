using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Text;

namespace IRSpeedyVPN.Common
{
    internal class SimpleDownloadManager:IDisposable
    {
        
        public event AsyncCompletedEventHandler DownloadFileCompleted;
        public event DownloadProgressChangedEventHandler DownloadProgressChanged;
        WebClient wc;
        public void DownloadAsync(string Url, string savepath, object state = null)
        {

            wc = new WebClient();
            wc.DownloadFileCompleted += DownloadFileCompleted;
            wc.DownloadProgressChanged += DownloadProgressChanged;
            wc.DownloadFileAsync(
                new System.Uri(Url),
                savepath,
                state ?? savepath
            );

        }
        public static string DownloadString(string Url)
        {
            WebClient client = new WebClient();
            return client.DownloadString(Url);
        }
        public static byte[] DownloadData(string Url)
        {
            WebClient client = new WebClient();
            return client.DownloadData(Url);
        }
        public void Dispose()
        {
            wc.Dispose();
        }
    }
}
