using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIInterface.SupportingClasses
{
    public class ReplacedId
    {
        [JsonProperty("requested")]
        public string Requested { get; set; }

        [JsonProperty("returned")]
        public string Returned { get; set; }
    }
}
