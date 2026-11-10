using Microsoft.EntityFrameworkCore;
using PhyloDivas.Data.New.Entities;


namespace PhyloDivas.Data.New.Data;
public partial class PhylodivasContext : DbContext
{
    public PhylodivasContext()
    {
    }

    public PhylodivasContext(DbContextOptions<PhylodivasContext> options) : base(options) { }

    public virtual DbSet<apiErrorLog> api_error_logs { get; set; }

    public virtual DbSet<gene> genes { get; set; }

    public virtual DbSet<geneAssemblyContext> gene_assembly_contexts { get; set; }

    public virtual DbSet<geneIdentifierXref> gene_identifier_xrefs { get; set; }

    public virtual DbSet<geneReferenceStandard> gene_reference_standards { get; set; }

    public virtual DbSet<geneSynonym> gene_synonyms { get; set; }

    public virtual DbSet<geneTranscriptQuick> gene_transcript_quicks { get; set; }

    public virtual DbSet<genomeAssembly> genome_assemblies { get; set; }

    public virtual DbSet<genomeAssemblyStat> genome_assembly_stats { get; set; }

    public virtual DbSet<genomeChromosome> genome_chromosomes { get; set; }

    public virtual DbSet<genomeDownloadAsset> genome_download_assets { get; set; }

    public virtual DbSet<genomeSpeciesSynonym> genome_species_synonyms { get; set; }

    public virtual DbSet<feedbackForm> feedback_forms { get; set; }

    public virtual DbSet<genomeSpecies> genome_species { get; set; }

    public virtual DbSet<searchHistory> search_histories { get; set; }

    public virtual DbSet<taxon> taxons { get; set; }

    public virtual DbSet<taxonRefreshLog> taxon_refresh_logs { get; set; }

    public virtual DbSet<User> Users { get; set; }


    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (optionsBuilder.IsConfigured)
            return;

        var connectionString = Environment.GetEnvironmentVariable("PHYLODIVAS_DB_CONNECTION");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            optionsBuilder.UseNpgsql(connectionString);
        }
    }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("uuid-ossp");

        modelBuilder.Entity<apiErrorLog>(entity =>
        {
            entity.HasKey(e => e.id).HasName("api_error_log_pkey");

            entity.Property(e => e.date_time).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<gene>(entity =>
        {
            entity.HasKey(e => e.gene_id).HasName("gene_pkey");

            entity.Property(e => e.gene_id).ValueGeneratedNever();

            entity.HasOne(d => d.tax).WithMany(p => p.genes)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("gene_tax_id_fkey");
        });

        modelBuilder.Entity<geneAssemblyContext>(entity =>
        {
            entity.HasKey(e => e.gene_assembly_context_id).HasName("gene_assembly_context_pkey");

            entity.HasOne(d => d.assembly).WithMany(p => p.gene_assembly_contexts).HasConstraintName("gene_assembly_context_assembly_id_fkey");

            entity.HasOne(d => d.gene).WithMany(p => p.gene_assembly_contexts).HasConstraintName("gene_assembly_context_gene_id_fkey");
        });

        modelBuilder.Entity<geneIdentifierXref>(entity =>
        {
            entity.HasKey(e => new { e.gene_id, e.source, e.identifier }).HasName("gene_identifier_xref_pkey");

            entity.HasOne(d => d.gene).WithMany(p => p.gene_identifier_xrefs).HasConstraintName("gene_identifier_xref_gene_id_fkey");
        });

        modelBuilder.Entity<geneReferenceStandard>(entity =>
        {
            entity.HasKey(e => e.gene_reference_standard_id).HasName("gene_reference_standard_pkey");

            entity.HasOne(d => d.gene).WithMany(p => p.gene_reference_standards).HasConstraintName("gene_reference_standard_gene_id_fkey");
        });

        modelBuilder.Entity<geneSynonym>(entity =>
        {
            entity.HasKey(e => new { e.gene_id, e.synonym }).HasName("gene_synonym_pkey");

            entity.HasOne(d => d.gene).WithMany(p => p.gene_synonyms).HasConstraintName("gene_synonym_gene_id_fkey");
        });

        modelBuilder.Entity<geneTranscriptQuick>(entity =>
        {
            entity.HasKey(e => new { e.gene_id, e.transcript_accession }).HasName("gene_transcript_quick_pkey");

            entity.HasOne(d => d.gene).WithMany(p => p.gene_transcript_quicks).HasConstraintName("gene_transcript_quick_gene_id_fkey");
        });

        modelBuilder.Entity<genomeAssembly>(entity =>
        {
            entity.HasKey(e => e.assembly_id).HasName("assembly_pkey");

            entity.Property(e => e.assembly_id).HasDefaultValueSql("nextval('assembly_assembly_id_seq'::regclass)");
            entity.Property(e => e.is_current_for_species).HasDefaultValue(false);

            entity.HasOne(d => d.tax).WithMany(p => p.genome_assemblies)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("assembly_tax_id_fkey");
        });

        modelBuilder.Entity<genomeAssemblyStat>(entity =>
        {
            entity.HasKey(e => e.assembly_id).HasName("assembly_stats_pkey");

            entity.Property(e => e.assembly_id).ValueGeneratedNever();

            entity.HasOne(d => d.assembly).WithOne(p => p.genome_assembly_stat).HasConstraintName("assembly_stats_assembly_id_fkey");
        });

        modelBuilder.Entity<genomeChromosome>(entity =>
        {
            entity.HasKey(e => e.chromosome_id).HasName("chromosome_pkey");

            entity.Property(e => e.chromosome_id).HasDefaultValueSql("nextval('chromosome_chromosome_id_seq'::regclass)");

            entity.HasOne(d => d.assembly).WithMany(p => p.genome_chromosomes).HasConstraintName("chromosome_assembly_id_fkey");
        });

        modelBuilder.Entity<genomeDownloadAsset>(entity =>
        {
            entity.HasKey(e => e.download_id).HasName("download_asset_pkey");

            entity.Property(e => e.download_id).HasDefaultValueSql("nextval('download_asset_download_id_seq'::regclass)");

            entity.HasOne(d => d.assembly).WithMany(p => p.genome_download_assets).HasConstraintName("download_asset_assembly_id_fkey");
        });

        modelBuilder.Entity<genomeSpeciesSynonym>(entity =>
        {
            entity.HasKey(e => new { e.tax_id, e.synonym }).HasName("species_synonym_pkey");

            entity.HasOne(d => d.tax).WithMany(p => p.genome_species_synonyms).HasConstraintName("species_synonym_tax_id_fkey");
        });

        modelBuilder.Entity<genomeSpecies>(entity =>
        {
            entity.HasKey(e => e.tax_id).HasName("species_pkey");

            entity.Property(e => e.tax_id).ValueGeneratedNever();
        });

        modelBuilder.Entity<searchHistory>(entity =>
        {
            entity.HasKey(e => e.search_id).HasName("search_history_pkey");

            entity.Property(e => e.searched_at).HasDefaultValueSql("now()");

            entity.HasOne(d => d.resolved_assembly).WithMany(p => p.search_histories)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("search_history_resolved_assembly_id_fkey");

            entity.HasOne(d => d.resolved_gene).WithMany(p => p.search_histories)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("search_history_resolved_gene_id_fkey");

            entity.HasOne(d => d.resolved_tax).WithMany(p => p.search_histories)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("search_history_resolved_tax_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.SearchHistories)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("search_history_user_id_fkey");
        });

        modelBuilder.Entity<taxon>(entity =>
        {
            entity.HasKey(e => e.tax_id).HasName("taxon_pkey");
            entity.Property(e => e.tax_id).ValueGeneratedNever();

            entity.HasOne(d => d.parent_tax).WithMany(p => p.Inverseparent_tax)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("taxon_parent_tax_id_fkey");
        });

        modelBuilder.Entity<taxonRefreshLog>(entity =>
        {
            entity.HasKey(e => e.id).HasName("taxon_refresh_log_pkey");

            entity.HasOne(d => d.tax).WithMany(p => p.taxon_refresh_logs).HasConstraintName("taxon_refresh_log_tax_id_fkey");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("users_pkey");

            entity.Property(e => e.UserId).HasDefaultValueSql("uuid_generate_v4()");
            entity.Property(e => e.State).IsFixedLength();
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<feedbackForm>(entity =>
        {
            entity.HasKey(e => e.feedback_id).HasName("feedback_form_pkey");

            entity.Property(e => e.date_submitted)
                  .HasDefaultValueSql("now()");

            entity.Property(e => e.contact_email)
                  .HasMaxLength(255);

            entity.Property(e => e.usage_frequency)
                  .HasMaxLength(50);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
