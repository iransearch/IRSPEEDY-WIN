using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IRSpeedyVPN.WebServices
{
    public class DefaultPlainResponse<T>
    {
        public T data { get; set; }
        public string message { get; set; }

    }
}
