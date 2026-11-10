using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIInterface.SupportingClasses
{
    public class GeneWarning
    {
        [JsonConverter(typeof(StringEnumConverter))]
        [JsonProperty("gene_warning_code")]
        public ENUMS.GeneWarningCode GeneWarningCode { get; set; }

        [JsonProperty("reason")]
        public string Reason { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("replaced_id")]
        public ReplacedId ReplacedId { get; set; }

        [JsonProperty("unrecognized_identifier")]
        public string UnrecognizedIdentifier { get; set; }
    }
}
