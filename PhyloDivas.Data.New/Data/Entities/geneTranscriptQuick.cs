using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PhyloDivas.Data.New.Entities;

[PrimaryKey("gene_id", "transcript_accession")]
[Table("gene_transcript_quick")]
public partial class geneTranscriptQuick
{
    [Key]
    public int gene_id { get; set; }

    [Key]
    [StringLength(32)]
    public string transcript_accession { get; set; } = null!;

    public int? length_nt { get; set; }

    [StringLength(32)]
    public string? select_category { get; set; }

    [ForeignKey("gene_id")]
    [InverseProperty("gene_transcript_quicks")]
    public virtual gene gene { get; set; } = null!;
}
