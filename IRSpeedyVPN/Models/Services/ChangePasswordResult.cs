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
        [JsonProperty("msg")]
        public string ErrorMessage { get; set; }
        [JsonProperty("st")]
        public bool IsSuccess { get; set; }

        [JsonProperty("code")]
        public int Code { get; set; }


    }
}
