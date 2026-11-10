using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIInterface.SupportingClasses
{
    public class GeneError
    {
        [JsonConverter(typeof(StringEnumConverter))]
        [JsonProperty("assembly_error_code")]
        public ENUMS.AssemblyErrorCode AssemblyErrorCode { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        [JsonProperty("gene_error_code")]
        public ENUMS.GeneErrorCode GeneErrorCode { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        [JsonProperty("organelle_error_code")]
        public ENUMS.OrganelleErrorCode OrganelleErrorCode { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        [JsonProperty("virus_error_code")]
        public ENUMS.VirusErrorCode VirusErrorCode { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        [JsonProperty("taxonomy_error_code")]
        public ENUMS.TaxonomyErrorCode TaxonomyErrorCode { get; set; }

        [JsonProperty("reason")]
        public string Reason { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("invalid_identifiers")]
        public List<string> InvalidIdentifiers { get; set; }
    }
}
