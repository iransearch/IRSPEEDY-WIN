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
        // Server-list payload keys, newest first. The server encrypts each response with
        // exactly one key (chosen from the client's "kv" flag), so we try the new key
        // first and fall back to the legacy key. This keeps decryption working whether the
        // server has already switched to the new key or is still on the old one.
        private static readonly string[] Keys =
        {
            "Kx7pQ2mZ9vL4nR8tW3sB6yD1", // kv=2 (new)
            "g82g2vu23o99t897c3299002", // kv=1 (legacy) — keep for backward compatibility
        };

        public T DecryptFromBase64<T>(string s)
        {
            var serializer = new JavaScriptSerializer();
            serializer.RegisterConverters(new JavaScriptConverter[] { AppServices.JsonConverter });
            return serializer.Deserialize<T>(DecryptFromBase64String(s));
        }

        public String DecryptFromBase64String(string s)
        {
            var cipher = s.FromBase64String();
            string lastResult = null;
            foreach (var key in Keys)
            {
                try
                {
                    var text = new TripleDesHelper(key.ToAsciiBytes()).Decrypt(cipher).ToUTF8String();
                    lastResult = text;
                    if (LooksLikeJson(text))
                        return text;
                }
                catch
                {
                    // Wrong key: padding/format error. Try the next key.
                }
            }

            // No key produced obvious JSON; return the last attempt (may be null) so the
            // caller/deserializer behaves exactly as it did with the previous single key.
            return lastResult;
        }

        private static bool LooksLikeJson(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            for (int i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                    continue;
                return c == '{' || c == '[';
            }
            return false;
        }
    }
}
