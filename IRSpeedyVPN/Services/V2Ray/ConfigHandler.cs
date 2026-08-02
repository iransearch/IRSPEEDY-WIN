using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using v2rayN.Mode;
using v2rayN.Base;
using System.Linq;
using System.Threading.Tasks;
using IRSpeedyVPN.Common;

namespace v2rayN.Handler
{
    /// <summary>
    /// 本软件配置文件处理类
    /// </summary>
    class ConfigHandler
    {
        private static string configRes = Global.ConfigFileName;
        private static readonly object objLock = new object();

        #region ConfigHandler


        /// <summary>
        /// 存储文件
        /// </summary>
        /// <param name="config"></param>
        private static void ToJsonFile(Config config)
        {
            return;
            lock (objLock)
            {
                try
                {

                    //save temp file
                    var resPath = Utils.GetPath(configRes);
                    var tempPath = $"{resPath}_temp";
                    if (Utils.ToJsonFile(config, tempPath) != 0)
                    {
                        return;
                    }

                    if (File.Exists(resPath))
                    {
                        File.Delete(resPath);
                    }
                    //rename
                    File.Move(tempPath, resPath);
                }
                catch (Exception ex)
                {
                    Utils.SaveLog("ToJsonFile", ex);
                }
            }
        }

        #endregion

        #region Server

        /// <summary>
        /// 添加服务器或编辑
        /// </summary>
        /// <param name="config"></param>
        /// <param name="vmessItem"></param>
        /// <returns></returns>
        public static int AddServer(ref Config config, VmessItem vmessItem, bool toFile = true)
        {
            vmessItem.configType = EConfigType.VMess;

            vmessItem.address = vmessItem.address.TrimEx();
            vmessItem.id = vmessItem.id.TrimEx();
            vmessItem.security = vmessItem.security.TrimEx();
            vmessItem.network = vmessItem.network.TrimEx();
            vmessItem.headerType = vmessItem.headerType.TrimEx();
            vmessItem.requestHost = vmessItem.requestHost.TrimEx();
            vmessItem.path = vmessItem.path.TrimEx();
            vmessItem.streamSecurity = vmessItem.streamSecurity.TrimEx();

            if (!Global.vmessSecuritys.Contains(vmessItem.security))
            {
                return -1;
            }

            AddServerCommon(ref config, vmessItem);

            if (toFile)
            {
                ToJsonFile(config);
            }
            return 0;
        }


      
       
       

        /// <summary>
        /// 添加自定义服务器
        /// </summary>
        /// <param name="config"></param>
        /// <param name="vmessItem"></param>
        /// <returns></returns>
        public static int AddCustomServer(ref Config config, VmessItem vmessItem, bool blDelete)
        {
            var fileName = vmessItem.address;
            if (!File.Exists(fileName))
            {
                return -1;
            }
            var ext = Path.GetExtension(fileName);
            string newFileName = $"{Utils.GetGUID()}{ext}";
            //newFileName = Path.Combine(Utils.GetTempPath(), newFileName);

            try
            {
                File.Copy(fileName, Utils.GetConfigPath(newFileName));
                if (blDelete)
                {
                    File.Delete(fileName);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog( ex);
                return -1;
            }

            vmessItem.address = newFileName;
            vmessItem.configType = EConfigType.Custom;
            if (Utils.IsNullOrEmpty(vmessItem.remarks))
            {
                vmessItem.remarks = $"import custom@{DateTime.Now.ToShortDateString()}";
            }


            AddServerCommon(ref config, vmessItem);

            ToJsonFile(config);

            return 0;
        }


        /// <summary>
        /// 添加服务器或编辑
        /// </summary>
        /// <param name="config"></param>
        /// <param name="vmessItem"></param>
        /// <returns></returns>
        public static int AddShadowsocksServer(ref Config config, VmessItem vmessItem, bool toFile = true)
        {
            vmessItem.configType = EConfigType.Shadowsocks;

            vmessItem.address = vmessItem.address.TrimEx();
            vmessItem.id = vmessItem.id.TrimEx();
            vmessItem.security = vmessItem.security.TrimEx();

            if (!LazyConfig.Instance.GetShadowsocksSecuritys(vmessItem).Contains(vmessItem.security))
            {
                return -1;
            }

            AddServerCommon(ref config, vmessItem);

            if (toFile)
            {
                ToJsonFile(config);
            }

            return 0;
        }

        /// <summary>
        /// 添加服务器或编辑
        /// </summary>
        /// <param name="config"></param>
        /// <param name="vmessItem"></param>
        /// <returns></returns>
        public static int AddSocksServer(ref Config config, VmessItem vmessItem, bool toFile = true)
        {
            vmessItem.configType = EConfigType.Socks;

            vmessItem.address = vmessItem.address.TrimEx();

            AddServerCommon(ref config, vmessItem);

            if (toFile)
            {
                ToJsonFile(config);
            }

            return 0;
        }

        /// <summary>
        /// 添加服务器或编辑
        /// </summary>
        /// <param name="config"></param>
        /// <param name="vmessItem"></param>
        /// <returns></returns>
        public static int AddTrojanServer(ref Config config, VmessItem vmessItem, bool toFile = true)
        {
            vmessItem.configType = EConfigType.Trojan;

            vmessItem.address = vmessItem.address.TrimEx();
            vmessItem.id = vmessItem.id.TrimEx();
            if (Utils.IsNullOrEmpty(vmessItem.streamSecurity))
            {
                vmessItem.streamSecurity = Global.StreamSecurity;
            }
            if (Utils.IsNullOrEmpty(vmessItem.allowInsecure))
            {
                vmessItem.allowInsecure = config.defAllowInsecure.ToString();
            }

            AddServerCommon(ref config, vmessItem);

            if (toFile)
            {
                ToJsonFile(config);
            }

            return 0;
        }
        public static int AddVlessServer(ref Config config, VmessItem vmessItem, bool toFile = true)
        {
            vmessItem.configType = EConfigType.VLESS;

            vmessItem.address = vmessItem.address.TrimEx();
            vmessItem.id = vmessItem.id.TrimEx();
            vmessItem.security = vmessItem.security.TrimEx();
            vmessItem.network = vmessItem.network.TrimEx();
            vmessItem.headerType = vmessItem.headerType.TrimEx();
            vmessItem.requestHost = vmessItem.requestHost.TrimEx();
            vmessItem.path = vmessItem.path.TrimEx();
            vmessItem.streamSecurity = vmessItem.streamSecurity.TrimEx();

            AddServerCommon(ref config, vmessItem);

            if (toFile)
            {
                ToJsonFile(config);
            }

            return 0;
        }


        /// <summary>
        /// 配置文件版本升级
        /// </summary>
        /// <param name="vmessItem"></param>
        /// <returns></returns>
        public static int UpgradeServerVersion(ref VmessItem vmessItem)
        {
            try
            {
                if (vmessItem == null
                    || vmessItem.configVersion == 2)
                {
                    return 0;
                }
                if (vmessItem.configType == EConfigType.VMess)
                {
                    string path = "";
                    string host = "";
                    string[] arrParameter;
                    switch (vmessItem.network)
                    {
                        case "kcp":
                            break;
                        case "ws":
                            //*ws(path+host),它们中间分号(;)隔开
                            arrParameter = vmessItem.requestHost.Replace(" ", "").Split(';');
                            if (arrParameter.Length > 0)
                            {
                                path = String.IsNullOrEmpty(arrParameter[0]) ? vmessItem.path : arrParameter[0];
                            }
                            if (arrParameter.Length > 1)
                            {
                                path = arrParameter[0];
                                host = arrParameter[1];
                            }
                            vmessItem.path = path;
                            vmessItem.requestHost = host;
                            break;
                        case "h2":
                            //*h2 path
                            arrParameter = vmessItem.requestHost.Replace(" ", "").Split(';');
                            if (arrParameter.Length > 0)
                            {
                                path = arrParameter[0];
                            }
                            if (arrParameter.Length > 1)
                            {
                                path = arrParameter[0];
                                host = arrParameter[1];
                            }
                            vmessItem.path = path;
                            vmessItem.requestHost = host;
                            break;
                        default:
                            break;
                    }
                }
                vmessItem.configVersion = 2;
            }
            catch
            {
            }
            return 0;
        }



        /// <summary>
        /// 添加服务器或编辑
        /// </summary>
        /// <param name="config"></param>
        /// <param name="vmessItem"></param>
        /// <returns></returns>




        public static int AddServerCommon(ref Config config, VmessItem vmessItem)
        {
            vmessItem.configVersion = 2;
            if (Utils.IsNullOrEmpty(vmessItem.allowInsecure))
            {
                vmessItem.allowInsecure = config.defAllowInsecure.ToString();
            }
            if (!Utils.IsNullOrEmpty(vmessItem.network) && !Global.networks.Contains(vmessItem.network))
            {
                vmessItem.network = Global.DefaultNetwork;
            }

            if (Utils.IsNullOrEmpty(vmessItem.indexId))
            {
                vmessItem.indexId = Utils.GetGUID(false);
            }           
            config.vmess = new List<VmessItem>();
            config.vmess.Add(vmessItem);
        

            return 0;
        }

   

     
        #endregion

        #region Batch add servers

        /// <summary>
        /// 批量添加服务器
        /// </summary>
        /// <param name="config"></param>
        /// <param name="clipboardData"></param>
        /// <param name="subid"></param>
        /// <returns>成功导入的数量</returns>
        private static int AddBatchServers(ref Config config, string clipboardData, string subid, List<VmessItem> lstOriSub, string groupId)
        {
            if (Utils.IsNullOrEmpty(clipboardData))
            {
                return -1;
            }

          
            //if (clipboardData.IndexOf("vmess") >= 0 && clipboardData.IndexOf("vmess") == clipboardData.LastIndexOf("vmess"))
            //{
            //    clipboardData = clipboardData.Replace("\r\n", "").Replace("\n", "");
            //}
            int countServers = 0;

            //string[] arrData = clipboardData.Split(new string[] { "\r\n" }, StringSplitOptions.None);
            string[] arrData = clipboardData.Split(Environment.NewLine.ToCharArray());
            foreach (string str in arrData)
            {
                //maybe sub
                if (Utils.IsNullOrEmpty(subid) && (str.StartsWith(Global.httpsProtocol) || str.StartsWith(Global.httpProtocol)))
                {
                    if (AddSubItem(ref config, str) == 0)
                    {
                        countServers++;
                    }
                    continue;
                }
                VmessItem vmessItem = ShareHandler.ImportFromConfigLink(str, out string msg);
                if (vmessItem == null)
                {
                    continue;
                }

               

                //groupId
                vmessItem.groupId = groupId;

                if (vmessItem.configType == EConfigType.VMess)
                {
                    if (AddServer(ref config, vmessItem, false) == 0)
                    {
                        countServers++;
                    }
                }
                else if (vmessItem.configType == EConfigType.Shadowsocks)
                {
                    if (AddShadowsocksServer(ref config, vmessItem, false) == 0)
                    {
                        countServers++;
                    }
                }
                else if (vmessItem.configType == EConfigType.Socks)
                {
                    if (AddSocksServer(ref config, vmessItem, false) == 0)
                    {
                        countServers++;
                    }
                }
                else if (vmessItem.configType == EConfigType.Trojan)
                {
                    if (AddTrojanServer(ref config, vmessItem, false) == 0)
                    {
                        countServers++;
                    }
                }
                else if (vmessItem.configType == EConfigType.VLESS)
                {
                    if (AddVlessServer(ref config, vmessItem, false) == 0)
                    {
                        countServers++;
                    }
                }
            }

            ToJsonFile(config);
            return countServers;
        }

        private static int AddBatchServers4Custom(ref Config config, string clipboardData, string subid, List<VmessItem> lstOriSub, string groupId)
        {
            if (Utils.IsNullOrEmpty(clipboardData))
            {
                return -1;
            }

            VmessItem vmessItem = new VmessItem();
            //Is v2ray configuration
            V2rayConfig v2rayConfig = Utils.FromJson<V2rayConfig>(clipboardData);
            if (v2rayConfig != null
                && v2rayConfig.inbounds != null
                && v2rayConfig.inbounds.Count > 0
                && v2rayConfig.outbounds != null
                && v2rayConfig.outbounds.Count > 0)
            {
                var fileName = Utils.GetTempPath($"{Utils.GetGUID(false)}.json");
                File.WriteAllText(fileName, clipboardData);

                vmessItem.coreType = ECoreType.Xray;
                vmessItem.address = fileName;
                vmessItem.remarks = "v2ray_custom";
            }
            //Is Clash configuration
            else if (clipboardData.IndexOf("port") >= 0
                && clipboardData.IndexOf("socks-port") >= 0
                && clipboardData.IndexOf("proxies") >= 0)
            {
                var fileName = Utils.GetTempPath($"{Utils.GetGUID(false)}.yaml");
                File.WriteAllText(fileName, clipboardData);

                vmessItem.coreType = ECoreType.clash;
                vmessItem.address = fileName;
                vmessItem.remarks = "clash_custom";
            }
            //Is hysteria configuration
            else if (clipboardData.IndexOf("server") >= 0
                && clipboardData.IndexOf("up") >= 0
                && clipboardData.IndexOf("down") >= 0
                && clipboardData.IndexOf("listen") >= 0
                && clipboardData.IndexOf("<html>") < 0
                && clipboardData.IndexOf("<body>") < 0)
            {
                var fileName = Utils.GetTempPath($"{Utils.GetGUID(false)}.json");
                File.WriteAllText(fileName, clipboardData);

                vmessItem.coreType = ECoreType.hysteria;
                vmessItem.address = fileName;
                vmessItem.remarks = "hysteria_custom";
            }
            //Is naiveproxy configuration
            else if (clipboardData.IndexOf("listen") >= 0
                && clipboardData.IndexOf("proxy") >= 0
                && clipboardData.IndexOf("<html>") < 0
                && clipboardData.IndexOf("<body>") < 0)
            {
                var fileName = Utils.GetTempPath($"{Utils.GetGUID(false)}.json");
                File.WriteAllText(fileName, clipboardData);

                vmessItem.coreType = ECoreType.naiveproxy;
                vmessItem.address = fileName;
                vmessItem.remarks = "naiveproxy_custom";
            }
            //Is Other configuration
            else
            {
                return -1;
                //var fileName = Utils.GetTempPath($"{Utils.GetGUID(false)}.txt");
                //File.WriteAllText(fileName, clipboardData);

                //vmessItem.address = fileName;
                //vmessItem.remarks = "other_custom";
            }

           
            if (lstOriSub != null && lstOriSub.Count == 1)
            {
                vmessItem.indexId = lstOriSub[0].indexId;
            }
            vmessItem.subid = subid;
            vmessItem.groupId = groupId;

            if (Utils.IsNullOrEmpty(vmessItem.address))
            {
                return -1;
            }

            if (AddCustomServer(ref config, vmessItem, true) == 0)
            {
                return 1;

            }
            else
            {
                return -1;
            }
        }

        private static int AddBatchServers4SsSIP008(ref Config config, string clipboardData, string subid, List<VmessItem> lstOriSub, string groupId)
        {
            if (Utils.IsNullOrEmpty(clipboardData))
            {
                return -1;
            }


            //SsSIP008
            var lstSsServer = Utils.FromJson<List<SsServer>>(clipboardData);
            if (lstSsServer == null || lstSsServer.Count <= 0)
            {
                var ssSIP008 = Utils.FromJson<SsSIP008>(clipboardData);
                if (ssSIP008?.servers != null && ssSIP008.servers.Count > 0)
                {
                    lstSsServer = ssSIP008.servers;
                }
            }

            if (lstSsServer != null && lstSsServer.Count > 0)
            {
                int counter = 0;
                foreach (var it in lstSsServer)
                {
                    var ssItem = new VmessItem()
                    {
                        subid = subid,
                        groupId = groupId,
                        remarks = it.remarks,
                        security = it.method,
                        id = it.password,
                        address = it.server,
                        port = Utils.ToInt(it.server_port)
                    };
                    if (AddShadowsocksServer(ref config, ssItem, false) == 0)
                    {
                        counter++;
                    }
                }
                ToJsonFile(config);
                return counter;
            }

            return -1;
        }

        public static int AddBatchServers(ref Config config, string clipboardData, string subid, string groupId)
        {
            List<VmessItem> lstOriSub = null;
            if (!Utils.IsNullOrEmpty(subid))
            {
                lstOriSub = config.vmess.Where(it => it.subid == subid).ToList();
            }

            int counter = AddBatchServers(ref config, clipboardData, subid, lstOriSub, groupId);
            if (counter < 1)
            {
                counter = AddBatchServers(ref config, Utils.Base64Decode(clipboardData), subid, lstOriSub, groupId);
            }

            if (counter < 1)
            {
                counter = AddBatchServers4SsSIP008(ref config, clipboardData, subid, lstOriSub, groupId);
            }

            //maybe other sub 
            if (counter < 1)
            {
                counter = AddBatchServers4Custom(ref config, clipboardData, subid, lstOriSub, groupId);
            }

            return counter;
        }


        #endregion

        #region Sub & Group

        /// <summary>
        /// add sub
        /// </summary>
        /// <param name="config"></param>
        /// <param name="url"></param>
        /// <returns></returns>
        public static int AddSubItem(ref Config config, string url)
        {
            //already exists
            if (config.subItem.FindIndex(e => e.url == url) >= 0)
            {
                return 0;
            }

            SubItem subItem = new SubItem
            {
                id = string.Empty,
                remarks = "import sub",
                url = url
            };
            config.subItem.Add(subItem);

            return SaveSubItem(ref config);
        }

        /// <summary>
        /// save sub
        /// </summary>
        /// <param name="config"></param>
        /// <returns></returns>
        public static int SaveSubItem(ref Config config)
        {
            if (config.subItem == null)
            {
                return -1;
            }

            foreach (var item in config.subItem.Where(item => Utils.IsNullOrEmpty(item.id)))
            {
                item.id = Utils.GetGUID(false);
            }

            ToJsonFile(config);
            return 0;
        }
        #endregion
     


        #region routing

        public static RoutingItem GetLockedRoutingItem(ref Config config)
        {
            if (config.routings == null)
            {
                return null;
            }
            return config.routings.Find(it => it.locked == true);
        }
#endregion
    }
}
