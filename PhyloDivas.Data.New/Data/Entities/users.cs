using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
namespace PhyloDivas.Data.New.Entities
{
    [Table("users")]
    public partial class User
    {
        [Key]
        [Column("user_id")]
        public Guid UserId { get; set; } = Guid.NewGuid();


        [Column("age")]
        public short? Age { get; set; }

        [Column("current_status")]
        [StringLength(64)]
        public string? CurrentStatus { get; set; }

        [Column("highest_degree")]
        [StringLength(64)]
        public string? HighestDegree { get; set; }

        [Column("associated_institution")]
        [StringLength(128)]
        public string? AssociatedInstitution { get; set; }

        [Column("state")]
        [StringLength(2)]
        public string? State { get; set; }

        [Column("country")]
        [StringLength(64)]
        public string? Country { get; set; }

        [Column("updated_at", TypeName = "timestamp with time zone")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public virtual ICollection<searchHistory> SearchHistories { get; set; } = new List<searchHistory>();

    }
}
