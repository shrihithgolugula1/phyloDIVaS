using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIInterface.SupportingClasses
{
    public class Gene
    {
        [JsonProperty("gene_id")]
        public ulong GeneId { get; set; }

        [JsonProperty("symbol")]
        public string? Symbol { get; set; }

        [JsonProperty("description")]
        public string? Description { get; set; }

        [JsonProperty("tax_id")]
        public ulong? TaxId { get; set; }

        [JsonProperty("taxname")]
        public string? TaxName { get; set; }

        [JsonProperty("common_name")]
        public string? CommonName { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        [JsonProperty("type")]
        public ENUMS.GeneType? Type { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        [JsonProperty("rna_type")]
        public ENUMS.RnaType? RnaType { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        [JsonProperty("orientation")]
        public ENUMS.Orientation? Orientation { get; set; }

        [JsonProperty("reference_standards")]
        public List<ReferenceStandard>? ReferenceStandards { get; set; }

        [JsonProperty("genomic_regions")]
        public List<GenomicRegion>? GenomicRegions { get; set; }

        [JsonProperty("chromosomes")]
        public List<string>? Chromosomes { get; set; }

        [JsonProperty("nomenclature_authority")]
        public NomenclatureAuthority? NomenclatureAuthority { get; set; }

        [JsonProperty("swiss_prot_accessions")]
        public List<string>? SwissProtAccessions { get; set; }

        [JsonProperty("ensembl_gene_ids")]
        public List<string>? EnsemblGeneIds { get; set; }

        [JsonProperty("omim_ids")]
        public List<string>? OmimIds { get; set; }

        [JsonProperty("synonyms")]
        public List<string>? Synonyms { get; set; }

        [JsonProperty("replaced_gene_id")]
        public ulong? ReplacedGeneId { get; set; }

        [JsonProperty("annotations")]
        public List<Annotation>? Annotations { get; set; }

        [JsonProperty("transcript_count")]
        public int? TranscriptCount { get; set; }

        [JsonProperty("protein_count")]
        public int? ProteinCount { get; set; }

        [JsonProperty("transcript_type_counts")]
        public List<TranscriptTypeCount>? TranscriptTypeCounts { get; set; }

        [JsonProperty("gene_groups")]
        public List<GeneGroup>? GeneGroups { get; set; }

        [JsonProperty("summary")]
        public List<GeneSummary>? Summary { get; set; }

        [JsonProperty("gene_ontology")]
        public GeneOntology? GeneOntology { get; set; }

        [JsonProperty("locus_tag")]
        public string? LocusTag { get; set; }
    }
}
