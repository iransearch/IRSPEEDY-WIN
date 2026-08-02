using IRSpeedyVPN.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Net;
using Ionic.Zip;
using System.IO;
using System.Reflection;
using IRSpeedyVPN.Common;
using IRSpeedyVPN.Resource;
using System.ComponentModel.Composition;

namespace IRSpeedyVPN.UserControls
{
    /// <summary>
    /// Interaction logic for UCUpdate.xaml
    /// </summary>
    public partial class UCUpdate : UserControl, IHasTitle
    {
        
        ResourceManager ResourceManager;
        public UCUpdate()
        {
            InitializeComponent();
        }
        string dlLink;
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "IRSpeedy");
        string filename;
        public string Title => "بروزرسانی";

        public void SetInfos(Version version, string dlLink, string comment)
        {
            this.dlLink = dlLink;
            txtComment.Text = comment;
            txtVersion.Text = version.ToString();
        } 
        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            ResourceManager = ((ResourceManager)Program.container.GetInstance(typeof(ResourceManager)));
        }

        private void btnDownload_Click(object sender, RoutedEventArgs e)
        {

//            Wc_DownloadFileCompleted(this, new System.ComponentModel.AsyncCompletedEventArgs(null, false, Path.Combine(ResourceManager.TempPath, System.IO.Path.GetFileName(new Uri(dlLink).AbsolutePath))));
  //          return;
            //Process.Start(dlLink);
            try
            {
                txbTitle.Text = "در حال دریافت بروزرسانی\n لطفا صبر نمائید";
                btnDownload.IsEnabled = false;
                gridExplain.Visibility = Visibility.Hidden;
                gridUpdate.Visibility = Visibility.Visible;
                Uri uri = new Uri(dlLink);
                filename = System.IO.Path.GetFileName(uri.AbsolutePath);                
                SimpleDownloadManager dl = new SimpleDownloadManager();
                dl.DownloadFileCompleted += Wc_DownloadFileCompleted;
                dl.DownloadProgressChanged += Wc_DownloadProgressChanged;
                dl.DownloadAsync(dlLink, Path.Combine(ResourceManager.TempPath, filename));

            }
            catch (Exception ex)
            {
                txbProgress.Text = ex.Message;
                txbTitle.Text = "دریافت بروزرسانی با مشکل مواجه شد \nلطفا مجددا تلاش نمایید";
                btnDownload.IsEnabled = true;
            }
        }

        private void Wc_DownloadFileCompleted(object sender, System.ComponentModel.AsyncCompletedEventArgs e)
        {
            try
            {
                if (e.Error == null)
                {
                    var file = File.OpenRead(e.UserState.ToString());

                    ResourceManager.Extract(file);
                    file.Close();
                    File.Delete(e.UserState.ToString());
                    string helperAddress = ResourceManager.TempPath + "\\misc\\udh.exe";
                    ShellExecute.ShellexecAndReturnProcess(helperAddress, $"\"{Process.GetCurrentProcess().MainModule.FileName}\"");                                                            
                    Environment.Exit(0);
                }
                else
                {
                    btnDownload.IsEnabled = true;
                    txbProgress.Text = e.Error.Message;
                    txbTitle.Text = "دریافت بروزرسانی با مشکل مواجه شد \nلطفا مجددا تلاش نمایید";                 
                }
            }
            catch(Exception exp)
            {
                txbProgress.Text = exp.Message;
                btnDownload.IsEnabled = true;
            }
        }

        private void Wc_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            progress.Value = e.ProgressPercentage;
            txbProgress.Text = e.ProgressPercentage.ToString()+"%";
        }
    }
}
