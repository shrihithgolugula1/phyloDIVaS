using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace APIInterface.SupportingClasses
{
    public class TaxonomyResponse
    {
        [JsonPropertyName("taxonomy_nodes")]
        public List<TaxonomyNode> TaxonomyNodes { get; set; }
    }
}
