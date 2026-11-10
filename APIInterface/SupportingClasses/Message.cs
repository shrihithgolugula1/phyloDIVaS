using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace APIInterface.SupportingClasses
{
    public class Message
    {
        [JsonProperty("error")]
        public ErrorInfo Error { get; set; }

        [JsonProperty("warning")]
        public WarningInfo Warning { get; set; }
    }
}
