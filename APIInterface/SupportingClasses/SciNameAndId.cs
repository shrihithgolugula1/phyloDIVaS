using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIInterface.SupportingClasses
{
    public class SciNameAndId
    {
        
        [JsonProperty("sci_name")]
        public string sciName {  get; set; }

        
        [JsonProperty("tax_id")]
        public int taxId { get; set; }
       
        [JsonProperty("common_name")]
        public string commonName { get; set; }

        [JsonProperty("matched_term")]
        public string matchedTerm { get; set; }

        [JsonProperty("rank")]
        public ENUMS.Rank rank { get; set; }


        [JsonProperty("group_name")]
        public string groupName { get; set; }


    }
}
