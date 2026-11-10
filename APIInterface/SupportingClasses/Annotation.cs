using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIInterface.SupportingClasses
{
    public class Annotation
    {
        [JsonProperty("assembly_accession")]
        public string AssemblyAccession { get; set; }

        [JsonProperty("assembly_name")]
        public string AssemblyName { get; set; }

        [JsonProperty("annotation_name")]
        public string AnnotationName { get; set; }

        [JsonProperty("annotation_release_date")]
        public string AnnotationReleaseDate { get; set; }

        [JsonProperty("genomic_locations")]
        public List<GenomicLocation> GenomicLocations { get; set; }
    }
}
