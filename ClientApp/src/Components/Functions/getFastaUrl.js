
const assemblySummaryUrls = {
    refseq: {
      vertebrate_mammalian: "https://ftp.ncbi.nlm.nih.gov/genomes/refseq/vertebrate_mammalian/assembly_summary.txt",
      vertebrate_other: "https://ftp.ncbi.nlm.nih.gov/genomes/refseq/vertebrate_other/assembly_summary.txt",
      invertebrate: "https://ftp.ncbi.nlm.nih.gov/genomes/refseq/invertebrate/assembly_summary.txt",
      plant: "https://ftp.ncbi.nlm.nih.gov/genomes/refseq/plant/assembly_summary.txt",
      fungi: "https://ftp.ncbi.nlm.nih.gov/genomes/refseq/fungi/assembly_summary.txt",
      protozoa: "https://ftp.ncbi.nlm.nih.gov/genomes/refseq/protozoa/assembly_summary.txt",
      bacteria: "https://ftp.ncbi.nlm.nih.gov/genomes/refseq/bacteria/assembly_summary.txt",
      archaea: "https://ftp.ncbi.nlm.nih.gov/genomes/refseq/archaea/assembly_summary.txt",
      viral: "https://ftp.ncbi.nlm.nih.gov/genomes/refseq/viral/assembly_summary.txt",
      metagenomes: "https://ftp.ncbi.nlm.nih.gov/genomes/refseq/metagenomes/assembly_summary.txt",
      other: "https://ftp.ncbi.nlm.nih.gov/genomes/refseq/other/assembly_summary.txt"
    },
    genbank: {
      vertebrate_mammalian: "https://ftp.ncbi.nlm.nih.gov/genomes/genbank/vertebrate_mammalian/assembly_summary.txt",
      vertebrate_other: "https://ftp.ncbi.nlm.nih.gov/genomes/genbank/vertebrate_other/assembly_summary.txt",
      invertebrate: "https://ftp.ncbi.nlm.nih.gov/genomes/genbank/invertebrate/assembly_summary.txt",
      plant: "https://ftp.ncbi.nlm.nih.gov/genomes/genbank/plant/assembly_summary.txt",
      fungi: "https://ftp.ncbi.nlm.nih.gov/genomes/genbank/fungi/assembly_summary.txt",
      protozoa: "https://ftp.ncbi.nlm.nih.gov/genomes/genbank/protozoa/assembly_summary.txt",
      bacteria: "https://ftp.ncbi.nlm.nih.gov/genomes/genbank/bacteria/assembly_summary.txt",
      archaea: "https://ftp.ncbi.nlm.nih.gov/genomes/genbank/archaea/assembly_summary.txt",
      viral: "https://ftp.ncbi.nlm.nih.gov/genomes/genbank/viral/assembly_summary.txt",
      metagenomes: "https://ftp.ncbi.nlm.nih.gov/genomes/genbank/metagenomes/assembly_summary.txt",
      other: "https://ftp.ncbi.nlm.nih.gov/genomes/genbank/other/assembly_summary.txt"
    }
  };
  
  async function parseAssemblySummary(url){
    const res = await fetch(url);
    if(!res.ok){throw new Error("Failed to fetch url: " + res.status, res.statusText)};
    const text = await res.text();
    const lines = text.split("\n").filter(l => l && !l.startsWith("#"));
    const assemblies = {};
    for(let line of lines){
      const cols = line.split("\t");
      const acc = cols[0]; //assembly accession
      const ftp = cols[19]; //ftp path
      if (acc && ftp) assemblies[acc] = ftp;
    }
    return assemblies;
  }
  
  // Try to find the accession in all categories
  export async function getFastaUrl(accession) {
    console.log("Searching for " + accession);
  
    //check refseq for accession
    for (const [category, url] of Object.entries(assemblySummaryUrls.refseq)) {
      try{
      const assemblies = await parseAssemblySummary(url);
      if (assemblies[accession]) {
        const ftpPath = assemblies[accession];
        const folderName = ftpPath.split("/").pop();
        const fastaUrl = `${ftpPath}/${folderName}_genomic.fna.gz`;
        console.log("Found in RefSeq " + category);
        return fastaUrl;
      }
      }
      catch (error){
        console.warn("Error checking RefSeq " + category + error.message);
      }
    }
    //if not found in refseq, check genbank
    for(const [category, url] of Object.entries(assemblySummaryUrls.genbank)){
      try{
        const assemblies = await parseAssemblySummary(url);
        if(assemblies[accession]){
          const ftpPath = assemblies[accession];
          const folderName = ftpPath.split("/").pop();
          const fastaUrl = `${ftpPath}/${folderName}_genomic.fna.gz`;
          console.log("Found in GenBank " + category);
          return fastaUrl;
        }
      }
      catch (error){
        console.warn("Error checking GenBank " + category + error.message);
      }
    }
    //not found in either
    throw new Error(`Accession ${accession} not found in any assembly summary`);
  }
  