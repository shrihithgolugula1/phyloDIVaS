using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIInterface.SupportingClasses
{
    public class NomenclatureAuthority
    {
        [JsonProperty("authority")]
        public string Authority { get; set; }

        [JsonProperty("identifier")]
        public string Identifier { get; set; }
    }
}
