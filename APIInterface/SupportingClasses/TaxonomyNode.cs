using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace APIInterface.SupportingClasses
{
    public class TaxonomyNode
    {
        [JsonPropertyName("query")]
        public List<string> Query { get; set; }

        [JsonPropertyName("taxonomy")]
        public Taxonomy Taxonomy { get; set; }

        [JsonPropertyName("errors")]
        public List<TaxonomyError> Errors { get; set; }
    }
}
