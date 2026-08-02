using IRSpeedyVPN.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace IRSpeedyVPN.Security
{
    class TripleDesHelper : IEncryptor, IDecryptor
    {
        private byte[] key;
        public TripleDesHelper(byte[] key)
        {
            this.key = key;
        }
        public TripleDesHelper(params object[] key)
        {
            List<byte> bKey = new List<byte>();
            foreach (object o in key)
            {
                if (o is string)
                {
                    bKey.Add(Convert.ToByte(o.ToString(), 16));
                }
                else if(o is int)
                {
                    bKey.Add((byte)(int)o);
                }
                else if(o is byte[])
                {
                    bKey.AddRange((byte[])o);
                }
            }

            this.key = bKey.ToArray();
        }
        public byte[] Decrypt(byte[] toDeccryptArray)
        {


            TripleDESCryptoServiceProvider tdes = new TripleDESCryptoServiceProvider();
            //set the secret key for the tripleDES algorithm
            tdes.Key = key;
            //mode of operation. there are other 4 modes. 
            //We choose ECB(Electronic code Book)

            tdes.Mode = CipherMode.ECB;
            //padding mode(if any extra byte added)
            tdes.Padding = PaddingMode.PKCS7;

            ICryptoTransform cTransform = tdes.CreateDecryptor();
            byte[] resultArray = cTransform.TransformFinalBlock(
                                 toDeccryptArray, 0, toDeccryptArray.Length);
            //Release resources held by TripleDes Encryptor                
            tdes.Clear();
            //return the Clear decrypted TEXT
            return resultArray;
        }


        public byte[] Encrypt(byte[] toEncryptArray)
        {
            TripleDESCryptoServiceProvider tdes = new TripleDESCryptoServiceProvider();
            //set the secret key for the tripleDES algorithm
            tdes.Key = key;
            //mode of operation. there are other 4 modes.
            //We choose ECB(Electronic code Book)
            tdes.Mode = CipherMode.ECB;
            //padding mode(if any extra byte added)

            tdes.Padding = PaddingMode.PKCS7;

            ICryptoTransform cTransform = tdes.CreateEncryptor();
            //transform the specified region of bytes array to resultArray
            byte[] resultArray =
              cTransform.TransformFinalBlock(toEncryptArray, 0,
              toEncryptArray.Length);
            //Release resources held by TripleDes Encryptor
            tdes.Clear();
            //Return the encrypted data into unreadable string format
            return resultArray;
        }
    }
}
