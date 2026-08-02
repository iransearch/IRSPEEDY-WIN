using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IRSpeedyVPN.Interfaces
{
    internal interface IEncryptor
    {
        byte[] Encrypt(byte[] toDeccryptArray);
    }
}
