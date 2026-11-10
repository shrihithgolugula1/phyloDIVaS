using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PhyloDivas.Data.New.Entities;

[Table("gene_assembly_context")]
[Index("gene_id", "assembly_id", Name = "gene_assembly_context_gene_id_assembly_id_key", IsUnique = true)]
[Index("assembly_id", Name = "idx_gac_assembly")]
[Index("gene_id", Name = "idx_gac_gene")]
public partial class geneAssemblyContext
{
    [Key]
    public long gene_assembly_context_id { get; set; }

    public int gene_id { get; set; }

    public string assembly_id { get; set; }

    [StringLength(64)]
    public string? annotation_name { get; set; }

    public DateOnly? annotation_release_date { get; set; }

    [StringLength(16)]
    public string? chromosome { get; set; }

    [StringLength(32)]
    public string? genomic_accession_version { get; set; }

    public long? range_start { get; set; }

    public long? range_end { get; set; }

    [StringLength(8)]
    public string? orientation { get; set; }

    [ForeignKey("assembly_id")]
    [InverseProperty("gene_assembly_contexts")]
    public virtual genomeAssembly assembly { get; set; } = null!;

    [ForeignKey("gene_id")]
    [InverseProperty("gene_assembly_contexts")]
    public virtual gene gene { get; set; } = null!;
}
