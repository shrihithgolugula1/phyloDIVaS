using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIInterface.SupportingClasses
{
    public class TaxonSuggestResponse
    {
        [JsonProperty("sci_name_and_ids")]
        public List<SciNameAndId> SciNameAndIds { get; set; }
    }
}
