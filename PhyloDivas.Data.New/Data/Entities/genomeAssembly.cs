using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PhyloDivas.Data.New.Entities;

[Table("genome_assembly")]
public partial class genomeAssembly
{
    [Key]

    public string assembly_id { get; set; }

    public int tax_id { get; set; }

    [StringLength(32)]
    public string? refseq_accession { get; set; }

    [StringLength(32)]
    public string? current_accession { get; set; }

    [StringLength(32)]
    public string? genbank_accession { get; set; }

    [StringLength(64)]
    public string? assembly_type { get; set; }

    [StringLength(64)]
    public string? assembly_level { get; set; }

    [StringLength(255)]
    public string? submitter { get; set; }

    public DateOnly? release_date { get; set; }

    [StringLength(64)]
    public string? synonym { get; set; }

    public bool? refseq_genbank_differs { get; set; }

    public string? differences_note { get; set; }

    [StringLength(32)]
    public string? bioproject_accession { get; set; }

    public bool? is_current_for_species { get; set; }

    [Column(TypeName = "timestamp without time zone")]
    public DateTime? last_refreshed_utc { get; set; }

    [InverseProperty("assembly")]
    public virtual ICollection<geneAssemblyContext> gene_assembly_contexts { get; set; } = new List<geneAssemblyContext>();

    [InverseProperty("assembly")]
    public virtual genomeAssemblyStat? genome_assembly_stat { get; set; }

    [InverseProperty("assembly")]
    public virtual ICollection<genomeChromosome> genome_chromosomes { get; set; } = new List<genomeChromosome>();

    [InverseProperty("assembly")]
    public virtual ICollection<genomeDownloadAsset> genome_download_assets { get; set; } = new List<genomeDownloadAsset>();

    [InverseProperty("resolved_assembly")]
    public virtual ICollection<searchHistory> search_histories { get; set; } = new List<searchHistory>();

    [ForeignKey("tax_id")]
    [InverseProperty("genome_assemblies")]
    public virtual genomeSpecies tax { get; set; } = null!;
}
