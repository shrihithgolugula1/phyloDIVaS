using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIInterface.SupportingClasses
{
    public class OntologyTerm
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("go_id")]
        public string GoId { get; set; }

        [JsonProperty("evidence_code")]
        public string EvidenceCode { get; set; }

        [JsonProperty("qualifier")]
        public string Qualifier { get; set; }

        [JsonProperty("reference")]
        public Reference Reference { get; set; }
    }
}
