using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIInterface.SupportingClasses
{
    public class AssemblyInfo
    {
        [JsonProperty("assembly_level")]
        public string AssemblyLevel { get; set; }

        [JsonProperty("assembly_status")]
        public string AssemblyStatus { get; set; }

        [JsonProperty("assembly_name")]
        public string AssemblyName { get; set; }

        [JsonProperty("submission_date")]
        public string SubmissionDate { get; set; }

        [JsonProperty("release_date")]
        public string ReleaseDate { get; set; }

        [JsonProperty("submitter")]
        public string Submitter { get; set; }


        [JsonProperty("assembly_type")]
        public string assembly_type {  get; set; }

        [JsonProperty("synonym")]
        public string synonym { get; set; }

        [JsonProperty("paired_assembly")]
        public paired_assembly paired_Assembly { get; set; }

        [JsonProperty("bioproject_accession")]
        public string bioproject_accession { get; set; }



    }
}
