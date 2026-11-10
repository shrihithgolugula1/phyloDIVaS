using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PhyloDivas.Data.New.Entities;

[Table("taxon")]
public partial class taxon
{
    [Key]
    public int tax_id { get; set; }

    [StringLength(255)]
    public string scientific_name { get; set; } = null!;

    [StringLength(255)]
    public string? common_name { get; set; }

    [StringLength(32)]
    public string? rank { get; set; }

    public int? parent_tax_id { get; set; }

    [Column(TypeName = "timestamp without time zone")]
    public DateTime? last_refreshed_utc { get; set; }

    [InverseProperty("parent_tax")]
    public virtual ICollection<taxon> Inverseparent_tax { get; set; } = new List<taxon>();

    [ForeignKey("parent_tax_id")]
    [InverseProperty("Inverseparent_tax")]
    public virtual taxon? parent_tax { get; set; }

    [InverseProperty("tax")]
    public virtual ICollection<taxonRefreshLog> taxon_refresh_logs { get; set; } = new List<taxonRefreshLog>();
}
