using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IRSpeedyVPN.Common.Exceptions
{
    internal class ProtocolNotFoundException : BaseException
    {
        public ProtocolNotFoundException(string Protocol) : base()
        {
            ExMessage = "پروتوکل انتخاب شده یافت نشد :" + Protocol;
        }
    }
}
