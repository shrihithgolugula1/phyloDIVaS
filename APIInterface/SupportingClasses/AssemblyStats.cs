using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Newtonsoft.Json;
using PhyloDivas.Data.New.Entities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace APIInterface.SupportingClasses
{
    public class AssemblyStats
    {
        [JsonProperty("total_number_of_chromosomes")]
        public int TotalChromosomes { get; set; }

        [JsonProperty("total_sequence_length")]
        public long TotalSequenceLength { get; set; }

        [JsonProperty("gc_percent")]
        public decimal GCPercent { get; set; }


        [JsonProperty("total_ungapped_length")]
        public string total_ungapped_length { get; set; }

        //number_of_contigs: integer
        [JsonProperty("number_of_contigs")]
        public int number_of_contigs { get; set; }

        //contig_n50: integer
        [JsonProperty("contig_n50")]
        public int contig_n50 { get; set; }

        //contig_l50: integer
        [JsonProperty("contig_l50")]
        public int contig_l50 { get; set; }

        //number_of_scaffolds: integer
        [JsonProperty("number_of_scaffolds")]
        public int number_of_scaffolds { get; set; }

        //scaffold_n50: integer
        [JsonProperty("scaffold_n50")]
        public int scaffold_n50 { get; set; }

        //scaffold_l50: integer
        [JsonProperty("scaffold_l50")]
        public int scaffold_l50 { get; set; }

        //gaps_between_scaffolds_count: integer
        [JsonProperty("gaps_between_scaffolds_count")]
        public int gaps_between_scaffolds_count { get; set; }

        //number_of_component_sequences: integer
        [JsonProperty("number_of_component_sequences")]
        public int number_of_component_sequences { get; set; }

        //atgc_count: uint64
        [JsonProperty("atgc_count")]
        public long atgc_count { get; set; }

        //gc_count: uint64
        [JsonProperty("gc_count")]
        public long gc_count { get; set; }

        //genome_coverage: string
        [JsonProperty("genome_coverage")]
        public string genome_coverage { get; set; }

        //number_of_organelles: integer
        [JsonProperty("number_of_organelles")]
        public int number_of_organelles { get; set; }

    }
    
}
