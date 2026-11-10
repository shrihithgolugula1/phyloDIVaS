using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIInterface.SupportingClasses
{
    public class Organism
    {
        [JsonProperty("tax_id")]
        public int? TaxId { get; set; }

        [JsonProperty("sci_name")]
        public string? ScientificName { get; set; }

        [JsonProperty("organism_name")]
        public string? OrganismName { get; set; }

        [JsonProperty("common_name")]
        public string? CommonName { get; set; }
    }
}
