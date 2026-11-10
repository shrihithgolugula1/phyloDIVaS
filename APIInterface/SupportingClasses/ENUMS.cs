using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIInterface.SupportingClasses
{
    public class ENUMS
    {
        public enum GeneType { UNKNOWN, tRNA, rRNA, snRNA, scRNA, snoRNA, PROTEIN_CODING, PSEUDO, TRANSPOSON, miscRNA, ncRNA, BIOLOGICAL_REGION, OTHER }
        public enum RnaType { rna_UNKNOWN, premsg, tmRna }
        public enum Orientation { none, plus, minus }
        public enum RegionType { UNKNOWN, REFSEQ_GENE, PSEUDOGENE, BIOLOGICAL_REGION, OTHER }
        public enum TranscriptType { UNKNOWN, PROTEIN_CODING, NON_CODING, PROTEIN_CODING_MODEL, NON_CODING_MODEL }
        public enum SelectCategory { SELECT_UNKNOWN, REFSEQ_SELECT, MANE_SELECT, MANE_PLUS_CLINICAL }
        public enum GeneWarningCode { UNKNOWN_GENE_WARNING_CODE, ACCESSION_VERSION_MISMATCH, REPLACED_GENE_ID, DISCONTINUED_GENE_ID, UNRECOGNIZED_GENE_ID, UNRECOGNIZED_GENE_SYMBOL, UNRECOGNIZED_ACCESSION, UNRECOGNIZED_TAX_TOKEN, NO_GENE_ANNOTATION_FOUND, ABOVE_SPECIES_TAXON }
        public enum AssemblyErrorCode { UNKNOWN_ASSEMBLY_ERROR_CODE, INVALID_BIOPROJECT_IDS, NO_ASSEMBLIES_FOR_BIOPROJECTS, INVALID_TAXON, MISSING_SEARCH_FIELD, INVALID_BIOSAMPLE_IDS, NO_ASSEMBLIES_FOR_BIOSAMPLE_IDS, NO_ASSEMBLIES_FOR_ASSEMBLY_NAMES, INVALID_WGS_ACCESSIONS, NO_ASSEMBLIES_FOR_WGS_ACCESSIONS }
        public enum GeneErrorCode { UNKNOWN_GENE_ERROR_CODE, INCOMPLETE_LOOKUP_SYMBOL, INVALID_TAXON_GENE_ARGUMENT }
        public enum OrganelleErrorCode { UNKNOWN_ORGANELLE_ERROR_CODE, INVALID_ORGANELLE_TAXON, NO_ORGANELLES_FOR_ACCESSION }
        public enum VirusErrorCode { UNKNOWN_VIRUS_ERROR_CODE }
        public enum TaxonomyErrorCode { UNKNOWN_TAXONOMY_ERROR_CODE, INVALID_TAXONOMY_TAXON }

        public enum Rank
        {
            NO_RANK,
            SUPERKINGDOM,
            DOMAIN,
            REALM,
            KINGDOM,
            SUBKINGDOM,
            SUPERPHYLUM,
            SUBPHYLUM,
            PHYLUM,
            CLADE,
            SUPERCLASS,
            CLASS,
            SUBCLASS,
            INFRACLASS,
            COHORT,
            SUBCOHORT,
            SUPERORDER,
            ORDER,
            SUBORDER,
            INFRAORDER,
            PARVORDER,
            SUPERFAMILY,
            FAMILY,
            SUBFAMILY,
            GENUS,
            SUBGENUS,
            SPECIES_GROUP,
            SPECIES_SUBGROUP,
            SPECIES,
            SUBSPECIES,
            TRIBE,
            SUBTRIBE,
            FORMA,
            VARIETAS,
            STRAIN,
            SECTION,
            SUBSECTION,
            PATHOGROUP,
            SUBVARIETY,
            GENOTYPE,
            SEROTYPE,
            ISOLATE,
            MORPH,
            SERIES,
            FORMA_SPECIALIS,
            SEROGROUP,
            BIOTYPE,
            ACELLULAR_ROOT,
            CELLULAR_ROOT
        }
    }
}
