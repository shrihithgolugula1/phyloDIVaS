using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIInterface
{
    public class OrganelleInfo
    {
        [JsonProperty("assembly_name")]
        public string AssemblyName { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }
    }
}
