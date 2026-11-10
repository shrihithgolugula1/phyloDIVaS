using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIInterface.SupportingClasses
{
    public class TranscriptTypeCount
    {
        [JsonConverter(typeof(StringEnumConverter))]
        [JsonProperty("type")]
        public ENUMS.TranscriptType Type { get; set; }

        [JsonProperty("count")]
        public int Count { get; set; }
    }
}
