using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PhyloDivas.Data.New.Entities;

[Table("search_history")]
[Index("resolved_assembly_id", Name = "idx_search_history_assembly")]
[Index("resolved_gene_id", Name = "idx_search_history_gene")]
[Index("resolved_tax_id", Name = "idx_search_history_tax")]
[Index("search_type", Name = "idx_search_history_type")]
[Index("user_id", "searched_at", Name = "idx_search_history_user", IsDescending = new[] { false, true })]
public partial class searchHistory
{
    [Key]
    public long search_id { get; set; }

    public Guid? user_id { get; set; }

    [Column(TypeName = "timestamp without time zone")]
    public DateTime searched_at { get; set; }

    [StringLength(16)]
    public string search_type { get; set; } = null!;

    public string query_text { get; set; } = null!;

    [StringLength(40)]
    public string? resolved_identifier { get; set; }

    public int? resolved_tax_id { get; set; }

    public int? resolved_gene_id { get; set; }

    public string? resolved_assembly_id { get; set; }

    [Column(TypeName = "jsonb")]
    public string? parameters { get; set; }

    public int? result_count { get; set; }

    [StringLength(16)]
    public string? status { get; set; }

    [Column(TypeName = "timestamp without time zone")]
    public DateTime? deleted_at { get; set; }

    [ForeignKey("resolved_assembly_id")]
    [InverseProperty("search_histories")]
    public virtual genomeAssembly? resolved_assembly { get; set; }

    [ForeignKey("resolved_gene_id")]
    [InverseProperty("search_histories")]
    public virtual gene? resolved_gene { get; set; }

    [ForeignKey("resolved_tax_id")]
    [InverseProperty("search_histories")]
    public virtual genomeSpecies? resolved_tax { get; set; }

    [ForeignKey("user_id")]
    public virtual User? User { get; set; }
}
