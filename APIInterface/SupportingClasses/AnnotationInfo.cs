using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIInterface.SupportingClasses
{
    public class AnnotationInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("provider")]
        public string Provider { get; set; }

        [JsonProperty("release_date")]
        public string ReleaseDate { get; set; }

        [JsonProperty("busco")]
        public BuscoMetrics? Busco { get; set; }
    }

    public class BuscoMetrics
    {
        [JsonProperty("complete")]
        public double? Complete { get; set; }
    }
}
