using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PhyloDivas.Data.New.Entities;

[Table("taxon_refresh_log")]
[Index("tax_id", Name = "idx_taxon_refresh_taxid")]
public partial class taxonRefreshLog
{
    [Key]
    public long id { get; set; }

    public int tax_id { get; set; }

    [StringLength(64)]
    public string source { get; set; } = null!;

    [StringLength(32)]
    public string status { get; set; } = null!;

    [Column(TypeName = "timestamp without time zone")]
    public DateTime refreshed_at { get; set; }

    public string? notes { get; set; }

    [ForeignKey("tax_id")]
    [InverseProperty("taxon_refresh_logs")]
    public virtual taxon tax { get; set; } = null!;
}
