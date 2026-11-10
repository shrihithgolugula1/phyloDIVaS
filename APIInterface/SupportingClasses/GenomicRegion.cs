using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIInterface.SupportingClasses
{
    public class GenomicRegion
    {
        [JsonProperty("gene_range")]
        public GeneRange GeneRange { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        [JsonProperty("type")]
        public ENUMS.RegionType Type { get; set; }
    }
}
