using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IRSpeedyVPN.Common.Exceptions
{
    internal class BaseException : Exception
    {
        internal string ExMessage { get; set; }
    }
}
