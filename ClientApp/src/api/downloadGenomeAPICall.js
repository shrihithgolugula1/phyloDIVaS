async function getGenomeFasta(accession) {
    const url = "https://api.ncbi.nlm.nih.gov/datasets/v2/genome/accession/" + accession  + "/download?include_annotation_type=GENOME_FASTA" ;

    const a = document.createElement('a');
    a.href = url;
    a.download = `Genome-${accession}.zip`;


    document.body.appendChild(a); 
    a.click();

   
    document.body.removeChild(a);
        
}

async function getGeneFasta(geneId) {
    const url = "https://api.ncbi.nlm.nih.gov/datasets/v2/gene/id/" + geneId  + "/download?include_annotation_type=FASTA_GENE" ;

    const a = document.createElement('a');
    a.href = url;
    a.download = `Gene-${geneId}.zip`;


    document.body.appendChild(a); 
    a.click();

   
    document.body.removeChild(a);
        
}

export {getGenomeFasta,getGeneFasta}
