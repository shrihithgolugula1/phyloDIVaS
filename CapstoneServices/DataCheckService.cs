using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhyloDivas.Data.New;
using PhyloDivas.Data.New.Entities;
using PhyloDivas.Data.New.Repositories;

namespace CapstoneServices;

public class DataCheckService
{
    private readonly IGenericRepository<gene> _geneRepo;
    private readonly IGenericRepository<genomeSpecies> _genomeSpecyRepo;
    private readonly IGenericRepository<genomeAssembly> _genomeAssemblyRepo;
    private readonly IGenericRepository<genomeAssemblyStat> _assemblyStatRepo;
    private readonly IGenericRepository<genomeChromosome> _chromosomeRepo;
    private readonly IGenericRepository<taxon> _taxonRepo;


    public DataCheckService(
        IGenericRepository<gene> geneRepo,
        IGenericRepository<genomeSpecies> genomeSpecyRepo,
        IGenericRepository<genomeAssembly> genomeAssemblyRepo,
        IGenericRepository<genomeAssemblyStat> genomeAssemblyStatRepo,
        IGenericRepository<genomeChromosome> genomeChromosomeRepo,
        IGenericRepository<taxon> taxonRepo)
    {
        _geneRepo = geneRepo;
        _genomeSpecyRepo = genomeSpecyRepo;
        _genomeAssemblyRepo = genomeAssemblyRepo;
        _assemblyStatRepo = genomeAssemblyStatRepo;
        _chromosomeRepo = genomeChromosomeRepo;
        _taxonRepo = taxonRepo;
    }


    public async Task<bool> HasExistingDataAsync(string query, CancellationToken ct = default)
    {
        query = query?.ToLower().Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(query)) return false;


        // Check gene table (optional, included for completeness)
        var geneResults = await _geneRepo.FindAsync(g =>
            g.symbol.ToLower().Contains(query) ||
            (g.description != null && g.description.ToLower().Contains(query)) ||
            (g.gene_type != null && g.gene_type.ToLower().Contains(query)) ||
            (g.summary_text != null && g.summary_text.ToLower().Contains(query)), ct);


        if (geneResults.Any()) return true;


        // Check genome_specy table
        var genomeResults = await _genomeSpecyRepo.FindAsync(g =>
            g.organism_name.ToLower().Contains(query) ||
            (g.common_name != null && g.common_name.ToLower().Contains(query)) ||
            g.tax_id.ToString() == query, ct);


        if (genomeResults.Any()) return true;


        // Check genome_assembly table
        var assemblyResults = await _genomeAssemblyRepo.FindAsync(g =>
            g.refseq_accession != null && g.refseq_accession.ToLower() == query, ct);


        if (assemblyResults.Any()) return true;


        // Check taxon table
        var taxonResults = await _taxonRepo.FindAsync(t =>
            t.scientific_name.ToLower().Contains(query) ||
            (t.common_name != null && t.common_name.ToLower().Contains(query)) ||
            t.tax_id.ToString() == query, ct);


        return taxonResults.Any();
    }


    public async Task<List<GenomeDisplay>> SearchLocalGenomeDataAsync(string query, CancellationToken ct)
    {
        query = query.ToLower();
        var results = new List<GenomeDisplay>();


        var speciesList = await _genomeSpecyRepo.FindAsync(g => g.organism_name.ToLower().Contains(query) || (g.common_name != null && g.common_name.ToLower().Contains(query)) ||
        g.tax_id.ToString() == query, ct);


        foreach (var species in speciesList)
        {
            var assemblies = await _genomeAssemblyRepo.FindAsync(a => a.tax_id == species.tax_id, ct);


            foreach (var assembly in assemblies)
            {
                var stat = (await _assemblyStatRepo.FindAsync(s => s.assembly_id == assembly.assembly_id, ct)).FirstOrDefault();
                var chromosomes = await _chromosomeRepo.FindAsync(c => c.assembly_id == assembly.assembly_id, ct);


                results.Add(MapToGenomeDisplay(species, assembly, stat, chromosomes));
            }
        }


        return results;
    }

    private GenomeDisplay MapToGenomeDisplay(genomeSpecies? species, genomeAssembly? assembly, genomeAssemblyStat? stat, IEnumerable<genomeChromosome>? chromosomes)
    {
        GenomeDisplay returnObject = new GenomeDisplay();
        if (assembly != null)
        {
            returnObject.Accession = assembly.refseq_accession;
            returnObject.AssemblyName = assembly.assembly_id;
            returnObject.AssemblyLevel = assembly.assembly_level;
        }
        if (species != null)
        {
            returnObject.ScientificName = species.organism_name;
            returnObject.CommonName = species.common_name;
        }
        if (chromosomes != null)
        {
            returnObject.GenomeSize = chromosomes.Sum(c => c.size_bp);
        }
        if(stat != null)
        {
            returnObject.TotalUngappedLength = stat.total_ungapped_length;
            returnObject.GapsBetweenScaffolds = stat?.gaps_between_scaffolds;


            returnObject.NumChromosomes = stat?.total_chromosomes;
            returnObject.NumOrganelles = stat?.num_organelles;
            returnObject.NumScaffolds = stat?.num_scaffolds;
            returnObject.ScaffoldN50 = stat?.scaffold_n50;
            returnObject.ScaffoldL50 = stat?.scaffold_l50;


            returnObject.NumContigs = stat?.num_contigs;
            returnObject.ContigN50 = stat?.contig_n50;
            returnObject.ContigL50 = stat?.contig_l50;


            returnObject.GCPercent = stat?.gc_percent;
        }

        return returnObject;
    }
}



    //private readonly IGenericRepository<gene> _geneRepo;
    //private readonly IGenericRepository<genomeSpecies> _genomeSpecyRepo;
    //private readonly IGenericRepository<genomeAssembly> _genomeAssemblyRepo;
    //private readonly IGenericRepository<taxon> _taxonRepo;

    //public DataCheckService(
    //    IGenericRepository<gene> geneRepo,
    //    IGenericRepository<genomeSpecies> genomeSpecyRepo,
    //    IGenericRepository<genomeAssembly> genomeAssemblyRepo,
    //    IGenericRepository<taxon> taxonRepo)
    //{
    //    _geneRepo = geneRepo;
    //    _genomeSpecyRepo = genomeSpecyRepo;
    //    _genomeAssemblyRepo = genomeAssemblyRepo;
    //    _taxonRepo = taxonRepo;
    //}

    //public async Task<bool> HasExistingDataAsync(string query, CancellationToken ct = default)
    //{
    //    query = query?.ToLower().Trim() ?? string.Empty;
    //    if (string.IsNullOrEmpty(query)) return false;

    //    // Check gene table (optional, included for completeness)
    //    var geneResults = await _geneRepo.FindAsync(g =>
    //        g.symbol.ToLower().Contains(query) ||
    //        (g.description != null && g.description.ToLower().Contains(query)) ||
    //        (g.gene_type != null && g.gene_type.ToLower().Contains(query)) ||
    //        (g.summary_text != null && g.summary_text.ToLower().Contains(query)), ct);

    //    if (geneResults.Any()) return true;

    //    // Check genome_specy table
    //    var genomeResults = await _genomeSpecyRepo.FindAsync(g =>
    //        g.organism_name.ToLower().Contains(query) ||
    //        (g.common_name != null && g.common_name.ToLower().Contains(query)) ||
    //        g.tax_id.ToString() == query, ct);

    //    if (genomeResults.Any()) return true;

    //    // Check genome_assembly table
    //    var assemblyResults = await _genomeAssemblyRepo.FindAsync(g =>
    //        g.refseq_accession != null && g.refseq_accession.ToLower() == query, ct);

    //    if (assemblyResults.Any()) return true;

    //    // Check taxon table
    //    var taxonResults = await _taxonRepo.FindAsync(t =>
    //        t.scientific_name.ToLower().Contains(query) ||
    //        (t.common_name != null && t.common_name.ToLower().Contains(query)) ||
    //        t.tax_id.ToString() == query, ct);

    //    return taxonResults.Any();
    //}

    //public async Task<List<GenomeDisplay>> GetDataAsync(string query, CancellationToken ct = default)
    //{
    //    query = query?.ToLower().Trim() ?? string.Empty;
    //    if (string.IsNullOrEmpty(query)) return new List<GenomeDisplay>();

    //    var results = new List<GenomeDisplay>();

    //    // Search genomeSpecies
    //    var genomeSpeciesList = await _genomeSpecyRepo.FindAsync(g =>
    //        g.organism_name.ToLower().Contains(query) ||
    //        (g.common_name != null && g.common_name.ToLower().Contains(query)) ||
    //        g.tax_id.ToString() == query, ct);

    //    foreach (var species in genomeSpeciesList)
    //    {
    //        var assemblies = await _genomeAssemblyRepo.FindAsync(a => a.tax_id == species.tax_id, ct);

    //        foreach (var assembly in assemblies)
    //        {
    //            results.Add(MapToGenomeDisplay(species, assembly));
    //        }
    //    }

    //    // Search genomeAssembly directly by accession
    //    var directAssemblies = await _genomeAssemblyRepo.FindAsync(a =>
    //        (a.refseq_accession != null && a.refseq_accession.ToLower() == query) ||
    //        (a.genbank_accession != null && a.genbank_accession.ToLower() == query) ||
    //        (a.current_accession != null && a.current_accession.ToLower() == query), ct);

    //    foreach (var assembly in directAssemblies)
    //    {
    //        var species = await _genomeSpecyRepo.GetByIdAsync(assembly.tax_id, ct);
    //        results.Add(MapToGenomeDisplay(species, assembly));
    //    }

    //    // Search taxon table
    //    var taxa = await _taxonRepo.FindAsync(t =>
    //        t.scientific_name.ToLower().Contains(query) ||
    //        (t.common_name != null && t.common_name.ToLower().Contains(query)) ||
    //        t.tax_id.ToString() == query, ct);

    //    foreach (var taxon in taxa)
    //    {
    //        var speciesList = await _genomeSpecyRepo.FindAsync(g => g.tax_id == taxon.tax_id, ct);
    //        var assemblies = await _genomeAssemblyRepo.FindAsync(a => a.tax_id == taxon.tax_id, ct);

    //        foreach (var assembly in assemblies)
    //        {
    //            var species = speciesList.FirstOrDefault(s => s.tax_id == assembly.tax_id);
    //            results.Add(MapToGenomeDisplay(species, assembly, taxon));
    //        }
    //    }

    //    return results.Distinct().ToList();
    //}

    //// Helper method to map DB entities to GenomeDisplay
    //private GenomeDisplay MapToGenomeDisplay(genomeSpecies? species, genomeAssembly assembly, taxon? taxon = null)
    //{
    //    var stats = assembly.genome_assembly_stat;

    //    return new GenomeDisplay
    //    {
    //        Accession = assembly.refseq_accession,
    //        AssemblyName = assembly.synonym,
    //        AssemblyLevel = assembly.assembly_level,
    //        ScientificName = species?.organism_name ?? taxon?.scientific_name,
    //        CommonName = species?.common_name ?? taxon?.common_name,
    //        GenomeSize = stats?.total_sequence_length ?? 0,
    //        TotalUngappedLength = stats?.total_ungapped_length ?? 0,
    //        GapsBetweenScaffolds = stats?.gaps_between_scaffolds ?? 0,
    //        NumChromosomes = stats?.total_chromosomes ?? assembly.genome_chromosomes.Count,
    //        NumOrganelles = stats?.num_organelles ?? 0,
    //        NumScaffolds = stats?.num_scaffolds ?? 0,
    //        ScaffoldN50 = stats?.scaffold_n50 ?? 0,
    //        ScaffoldL50 = stats?.scaffold_l50 ?? 0,
    //        NumContigs = stats?.num_contigs ?? 0,
    //        ContigN50 = stats?.contig_n50 ?? 0,
    //        ContigL50 = stats?.contig_l50 ?? 0,
    //        GCPercent = stats?.gc_percent ?? 0,
    //        GenomeProvider = assembly.submitter,
    //        GenomeName = assembly.synonym,
    //        GenomeDate = assembly.release_date?.ToString("yyyy-MM-dd")
    //    };
    //}









    // public async Task<List<GenomeDisplay>> GetDataAsync(string query, CancellationToken ct = default)
    // {
    //     query = query?.ToLower().Trim() ?? string.Empty;
    //     if (string.IsNullOrEmpty(query)) return new List<GenomeDisplay>();

    //     var results = new List<GenomeDisplay>();

    //     // Search genome_specy
    //     var genomeSpecies = await _genomeSpecyRepo.FindAsync(g =>
    //         g.organism_name.ToLower().Contains(query) ||
    //         (g.common_name != null && g.common_name.ToLower().Contains(query)) ||
    //         g.tax_id.ToString() == query, ct);

    //     // Combine with related assemblies
    //     foreach (var species in genomeSpecies)
    //     {
    //         var relatedAssemblies = await _genomeAssemblyRepo.FindAsync(a => a.tax_id == species.tax_id, ct);
    //         foreach (var assembly in relatedAssemblies)
    //         {
    //             results.Add(new GenomeDisplay
    //             {
    //                 Accession = assembly.refseq_accession,
    //                 AssemblyName = assembly.synonym,
    //                 CommonName = species.common_name,
    //                 ScientificName = species.organism_name
    //             });
    //         }
    //     }

    //     // Search genome_assembly directly
    //     var assemblies = await _genomeAssemblyRepo.FindAsync(g =>
    //         (g.refseq_accession != null && g.refseq_accession.ToLower() == query) ||
    //         (g.genbank_accession != null && g.genbank_accession.ToLower() == query) ||
    //         (g.current_accession != null && g.current_accession.ToLower() == query), ct);

    //     foreach (var assembly in assemblies)
    //     {
    //         var species = await _genomeSpecyRepo.GetByIdAsync(assembly.tax_id, ct);
    //         if (species != null)
    //         {
    //             results.Add(new GenomeDisplay
    //             {
    //                 Accession = assembly.refseq_accession,
    //                 AssemblyName = assembly.synonym,
    //                 CommonName = species.common_name,
    //                 ScientificName = species.organism_name
    //             });
    //         }
    //     }

    //     // Search taxon table
    //     var taxa = await _taxonRepo.FindAsync(t =>
    //         t.scientific_name.ToLower().Contains(query) ||
    //         (t.common_name != null && t.common_name.ToLower().Contains(query)) ||
    //         t.tax_id.ToString() == query, ct);

    //     foreach (var taxon in taxa)
    //     {
    //         var species = await _genomeSpecyRepo.FindAsync(g => g.tax_id == taxon.tax_id, ct);
    //         var relatedAssemblies = await _genomeAssemblyRepo.FindAsync(a => a.tax_id == taxon.tax_id, ct);
    //         foreach (var assembly in relatedAssemblies)
    //         {
    //             var specy = species.FirstOrDefault(s => s.tax_id == assembly.tax_id);
    //             results.Add(new GenomeDisplay
    //             {
    //                 Accession = assembly.refseq_accession,
    //                 AssemblyName = assembly.synonym,
    //                 CommonName = specy?.common_name ?? taxon.common_name,
    //                 ScientificName = specy?.organism_name ?? taxon.scientific_name
    //             });
    //         }
    //     }

    //     return results.Distinct().ToList();
    // }
