using IRSpeedyVPN.Common;
using IRSpeedyVPN.Common.Json;
using IRSpeedyVPN.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace IRSpeedyVPN.WebServices
{
    internal class NewServiceDecryptor
    {
        TripleDesHelper tdes;
        string key = "g82g2vu23o99t897c3299002";

        public NewServiceDecryptor()
        {
            tdes = new TripleDesHelper(key.ToAsciiBytes());
        }
        public T DecryptFromBase64<T>(string s)
        {
            var serializer = new JavaScriptSerializer();
            serializer.RegisterConverters(new JavaScriptConverter[] { AppServices.JsonConverter });
            return serializer.Deserialize<T>(DecryptFromBase64String(s));
        }
        public String DecryptFromBase64String(string s)
        {            
            var sdata = tdes.Decrypt(s.FromBase64String()).ToUTF8String();
            return sdata;
        }

    }
}
