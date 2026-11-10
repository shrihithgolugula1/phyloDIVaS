using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIInterface.SupportingClasses
{
    public class GeneReport
    {
        [JsonProperty("gene")]
        public Gene? Gene { get; set; }

        [JsonProperty("product")]
        public GeneProduct? Product { get; set; }

        [JsonProperty("query")]
        public List<string>? Query { get; set; }

        [JsonProperty("warnings")]
        public List<GeneWarning>? Warnings { get; set; }

        [JsonProperty("warning")]
        public WarningInfo? Warning { get; set; }

        [JsonProperty("errors")]
        public List<GeneError>? Errors { get; set; }
    }
}
