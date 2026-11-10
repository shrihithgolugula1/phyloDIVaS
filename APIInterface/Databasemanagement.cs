using APIInterface.SupportingClasses;
using PhyloDivas.Data.New.Entities;
using Microsoft.EntityFrameworkCore;
using PhyloDivas.Data.New;
using PhyloDivas.Data.New.Entities;
using PhyloDivas.Data.New.Repositories;
using System.ComponentModel.DataAnnotations.Schema;
using PhylodivasContext = PhyloDivas.Data.New.Data.PhylodivasContext;
using CapstoneDesign;


namespace APIInterface
{
    public class Databasemanagement
    {

        public apiErrorLog CreateErrorLog(string call, string request, string response)
        {
            apiErrorLog returnObject = new apiErrorLog();
            returnObject.api_call = call;
            returnObject.request_obj = request;
            returnObject.response_obj = response;
            //returnObject.date_time = DateTime.UtcNow;
            return returnObject;
        }   


        //convert gene report
        public gene ConvertGeneReport(Gene geneObject)
        {
            
            gene returnObject = new gene();

            //gene Id
            returnObject.gene_id = (int)geneObject.GeneId;


            //tax id
            returnObject.tax_id = (int)geneObject.TaxId;


            //symbol
            returnObject.symbol = geneObject.Symbol;

            //description
            returnObject.description = geneObject.Description;

            //gene type
            returnObject.gene_type = geneObject.Type.ToString();

            //summary text
            returnObject.summary_text = geneObject.Summary.ToString();

            //transcript count
            returnObject.transcript_count = geneObject.TranscriptCount;


            //protein_count
            returnObject.protein_count = geneObject.ProteinCount;


            //mane_select_transcript
                //returnObject.mane_select_transcript = geneObject
                    //find out where to get this



            //last_refreshed_utc now
            //returnObject.last_refreshed_utc = DateTime.Now.ToUniversalTime();

            //genome_specy
            returnObject.tax = ConvertToGenomeSpecy(geneObject);


            return returnObject;
        }


        //convert genome report
        public genomeAssembly ConvertGenomeReport(GenomeReport genomeReport) 
        {
           genomeAssembly returnObject = new genomeAssembly();

            if (genomeReport.AssemblyInfo != null)
            {
                returnObject.assembly_id = genomeReport.AssemblyInfo.AssemblyName;
            }
            if (genomeReport.Organism != null)
            {
                returnObject.tax_id = (int)genomeReport.Organism.TaxId;
            }


            returnObject.refseq_accession = genomeReport.Accession;


            returnObject.current_accession = genomeReport.CurrentAccession;

            if (genomeReport.AdditionalSubmitters != null) 
            { 
                returnObject.genbank_accession = genomeReport.AdditionalSubmitters.genbank_accession; 
            }
    
            if(genomeReport.AssemblyInfo != null) { returnObject.assembly_type = genomeReport.AssemblyInfo.assembly_type; }

            if (genomeReport.AssemblyInfo != null)
            {
                returnObject.assembly_level = genomeReport.AssemblyInfo.AssemblyLevel;
            }

            if (genomeReport.AdditionalSubmitters != null)
            {
                returnObject.submitter = genomeReport.AdditionalSubmitters.submitter;
            }

            if (genomeReport.AssemblyInfo != null)
            {
                returnObject.release_date = DateOnly.Parse(genomeReport.AssemblyInfo.ReleaseDate);
            }

            if (genomeReport.AssemblyInfo != null)
            {
                returnObject.synonym = genomeReport.AssemblyInfo.synonym;
            }

            if (genomeReport.AssemblyInfo != null)
            {
                if (genomeReport.AssemblyInfo.paired_Assembly != null)
                {
                    returnObject.refseq_genbank_differs = genomeReport.AssemblyInfo.paired_Assembly.refseq_genbank_are_different;
                }
            }
            if (genomeReport.AssemblyInfo != null)
            {
                returnObject.differences_note = genomeReport.AssemblyInfo.paired_Assembly.differences;
            }


            if (genomeReport.AssemblyInfo != null)
            {
                returnObject.bioproject_accession = genomeReport.AssemblyInfo.bioproject_accession;
            }

            if (genomeReport.AssemblyInfo != null)
            {
                //is current for species
                if (genomeReport.AssemblyInfo.AssemblyStatus == "current")
                {
                    returnObject.is_current_for_species = true;
                }
                else
                {
                    returnObject.is_current_for_species = false;
                }
            }


            //returnObject.last_refreshed_utc = DateTime.Now.ToUniversalTime();

            returnObject.genome_assembly_stat = ConvertToGenomeAssemblyStat(genomeReport);


            returnObject.tax = ConvertToGenomeSpecy(genomeReport);
            
            return returnObject;
        }

        //convert taxon report
        public taxon ConvertTaxonReport(SciNameAndId species)
        {
            taxon returnObject = new taxon();

            returnObject.tax_id = species.taxId;

            returnObject.scientific_name = species.sciName;

            returnObject.common_name = species.commonName;

            returnObject.rank = species.rank.ToString();

            //returnObject.last_refreshed_utc = DateTime.Now.ToUniversalTime();

            return returnObject;
        }



        //convert to genome_specy
        public genomeSpecies ConvertToGenomeSpecy(Gene geneObject)
        {
            genomeSpecies returnObject = new genomeSpecies();
            returnObject.tax_id = (int)geneObject.TaxId;
            returnObject.organism_name = geneObject.TaxName;
            returnObject.common_name = geneObject.CommonName;
            return returnObject;
        }

        public genomeSpecies ConvertToGenomeSpecy(GenomeReport genomeObject)
        {
            genomeSpecies returnObject = new genomeSpecies();
            if (genomeObject.Organism != null)
            {
                returnObject.tax_id = (int)genomeObject.Organism.TaxId;
                if(genomeObject.Organism.ScientificName != null) { returnObject.organism_name = genomeObject.Organism.ScientificName; }
                else if(genomeObject.Organism.OrganismName != null) { returnObject.organism_name = genomeObject.Organism.OrganismName; }
                else { returnObject.organism_name = "unknown"; }
                returnObject.common_name = genomeObject.Organism.CommonName;
            }
            return returnObject;
        }


        public genomeAssemblyStat ConvertToGenomeAssemblyStat(GenomeReport report)
        {
            genomeAssemblyStat returnObject = new genomeAssemblyStat();
            if(report.AssemblyInfo != null)
            {
                returnObject.assembly_id = report.AssemblyInfo.AssemblyName;
            }

            if (report.AssemblyStats != null)
            {


                returnObject.total_chromosomes = report.AssemblyStats.TotalChromosomes;


                //public long? total_sequence_length { get; set; }
                returnObject.total_sequence_length = report.AssemblyStats.TotalSequenceLength;

                //public long? total_ungapped_length { get; set; }
                returnObject.total_ungapped_length = long.Parse(report.AssemblyStats.total_ungapped_length);

                //public int? num_contigs { get; set; }
                returnObject.num_contigs = report.AssemblyStats.number_of_contigs;

                //public long? contig_n50 { get; set; }
                returnObject.contig_n50 = report.AssemblyStats.contig_n50;

                //public int? contig_l50 { get; set; }
                returnObject.contig_l50 = report.AssemblyStats.contig_l50;

                //public int? num_scaffolds { get; set; }
                returnObject.num_scaffolds = report.AssemblyStats.number_of_scaffolds;

                //public long? scaffold_n50 { get; set; }
                returnObject.scaffold_n50 = report.AssemblyStats.scaffold_n50;

                //public int? scaffold_l50 { get; set; }
                returnObject.scaffold_l50 = report.AssemblyStats.scaffold_l50;

                //public int? gaps_between_scaffolds { get; set; }
                returnObject.gaps_between_scaffolds = report.AssemblyStats.gaps_between_scaffolds_count;

                //public decimal? gc_percent { get; set; }
                returnObject.gc_percent = report.AssemblyStats.GCPercent;

                //public int? num_organelles { get; set; }
                returnObject.num_organelles = report.AssemblyStats.number_of_organelles;
            }

            return returnObject;
        }



        public async void AddReport(Gene geneObject)
        {
            try
            {
                PhylodivasContext ctx = new PhylodivasContext();
                GenericRepository<gene> geneRepo = new GenericRepository<gene>(ctx);
                GenericRepository<genomeSpecies> specyRepo = new GenericRepository<genomeSpecies>(ctx);
                gene newGene = ConvertGeneReport(geneObject);

                genomeSpecies newSpecy = newGene.tax;

                

                await geneRepo.AddAsync(newGene);

                await specyRepo.AddAsync(newSpecy);

                await ctx.SaveChangesAsync();

            }
            catch (Exception ex) 
            {
                //add error logging 
                AddError(ex.TargetSite.ToString(), ex.StackTrace, ex.Message);
            }
        }


        public async void AddReport(GenomeReport genomeObject)
        {
            try
            {
                PhylodivasContext ctx = new PhylodivasContext();
                GenericRepository<genomeAssembly> genomeRepo = new GenericRepository<genomeAssembly>(ctx);
                GenericRepository<genomeSpecies> specyRepo = new GenericRepository<genomeSpecies>(ctx);
                genomeAssembly newGenome = ConvertGenomeReport(genomeObject);

                genomeSpecies newSpecy = newGenome.tax;

                await genomeRepo.AddAsync(newGenome);

                await specyRepo.AddAsync(newSpecy);

                await ctx.SaveChangesAsync();

            }
            catch (Exception ex)
            {
                //add error logging 
                AddError(ex.TargetSite.ToString(), ex.StackTrace, ex.Message);
            }
        }


        public async void AddReport(SciNameAndId taxon)
        {
            try
            {
                PhylodivasContext ctx = new PhylodivasContext();
                GenericRepository<taxon> taxonRepo = new GenericRepository<taxon>(ctx);
                taxon taxonObject = ConvertTaxonReport(taxon);


                await taxonRepo.AddAsync(taxonObject);


                await ctx.SaveChangesAsync();

            }
            catch (Exception ex)
            {
                //add error logging 
                AddError(ex.TargetSite.ToString(), ex.StackTrace, ex.Message);
            }
        }


        public async void AddError(string call, string request, string response)
        {
            try
            {
                PhylodivasContext ctx = new PhylodivasContext();
                GenericRepository<apiErrorLog> errorRepo = new GenericRepository<apiErrorLog>(ctx);
                apiErrorLog errorObject = CreateErrorLog(call, request, response);


                await errorRepo.AddAsync(errorObject);


                await ctx.SaveChangesAsync();

            }
            catch (Exception ex)
            {
                //well not much can be done here unless we want to get into an infinite loop
            }
        }


    }
}
