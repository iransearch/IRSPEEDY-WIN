using IRSpeedyVPN.Common.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IRSpeedyVPN.Models.Services
{
    [JsonConvertible]
    public class ChangePasswordResult
    {
        [JsonProperty("st")]
        public string ErrorMessage { get; set; }
        [JsonProperty("ChangePasswordStatusisTrue")]
        public bool IsSuccess { get; set; }


    }
}
