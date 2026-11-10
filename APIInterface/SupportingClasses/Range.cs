using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIInterface.SupportingClasses
{
    public class Range
    {
        [JsonProperty("begin")]
        public ulong Begin { get; set; }

        [JsonProperty("end")]
        public ulong End { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        [JsonProperty("orientation")]
        public ENUMS.Orientation Orientation { get; set; }

        [JsonProperty("order")]
        public int Order { get; set; }

        [JsonProperty("ribosomal_slippage")]
        public int RibosomalSlippage { get; set; }
    }
}
