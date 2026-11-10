using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PhyloDivas.Data.New.Entities;

[Table("gene")]
public partial class gene
{
    [Key]
    public int gene_id { get; set; }

    public int tax_id { get; set; }

    [StringLength(64)]
    public string symbol { get; set; } = null!;

    public string? description { get; set; }

    [StringLength(32)]
    public string? gene_type { get; set; }

    public string? summary_text { get; set; }

    public int? transcript_count { get; set; }

    public int? protein_count { get; set; }

    [StringLength(32)]
    public string? mane_select_transcript { get; set; }

    [Column(TypeName = "timestamp without time zone")]
    public DateTime? last_refreshed_utc { get; set; }

    [InverseProperty("gene")]
    public virtual ICollection<geneAssemblyContext> gene_assembly_contexts { get; set; } = new List<geneAssemblyContext>();

    [InverseProperty("gene")]
    public virtual ICollection<geneIdentifierXref> gene_identifier_xrefs { get; set; } = new List<geneIdentifierXref>();

    [InverseProperty("gene")]
    public virtual ICollection<geneReferenceStandard> gene_reference_standards { get; set; } = new List<geneReferenceStandard>();

    [InverseProperty("gene")]
    public virtual ICollection<geneSynonym> gene_synonyms { get; set; } = new List<geneSynonym>();

    [InverseProperty("gene")]
    public virtual ICollection<geneTranscriptQuick> gene_transcript_quicks { get; set; } = new List<geneTranscriptQuick>();

    [InverseProperty("resolved_gene")]
    public virtual ICollection<searchHistory> search_histories { get; set; } = new List<searchHistory>();

    [ForeignKey("tax_id")]
    [InverseProperty("genes")]
    public virtual genomeSpecies tax { get; set; } = null!;
}
