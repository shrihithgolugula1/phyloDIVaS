async function downloadSequence(url, filename) {
    const response = await fetch(url);
    if (!response.ok) {
        throw new Error(`NCBI download failed (HTTP ${response.status}). Please try again.`);
    }
    const blob = await response.blob();
    const signature = new Uint8Array(await blob.slice(0, 4).arrayBuffer());
    if (signature.length < 4 || signature[0] !== 0x50 || signature[1] !== 0x4b) {
        throw new Error('NCBI did not return a ZIP archive. No file was saved; please retry.');
    }
    const objectUrl = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    try {
        anchor.href = objectUrl;
        anchor.download = filename;
        document.body.appendChild(anchor);
        anchor.click();
    } finally {
        anchor.remove();
        // Allow the browser to start consuming the download before releasing it.
        setTimeout(() => URL.revokeObjectURL(objectUrl), 1000);
    }
}

async function getGenomeFasta(accession) {
    return downloadSequence(
        `https://api.ncbi.nlm.nih.gov/datasets/v2/genome/accession/${encodeURIComponent(accession)}/download?include_annotation_type=GENOME_FASTA`,
        `Genome-${accession}.zip`
    );
}

async function getGeneFasta(geneId) {
    return downloadSequence(
        `https://api.ncbi.nlm.nih.gov/datasets/v2/gene/id/${encodeURIComponent(geneId)}/download?include_annotation_type=FASTA_GENE`,
        `Gene-${geneId}.zip`
    );
}

export { getGenomeFasta, getGeneFasta };
