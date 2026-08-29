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
        // This build accepts only the new server-list key (kv=2). The legacy key is not
        // used, so the server MUST be serving the new key before this build is released,
        // otherwise responses cannot be decrypted.
        TripleDesHelper tdes;
        string key = "Kx7pQ2mZ9vL4nR8tW3sB6yD1";

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
