using System;

namespace CapstoneServices;

public class GenomeDisplay
{
    private string accession;
    private string scientificName;
    private string commonName;
    private string assemblyName;
    private string assemblyLevel;
    private long? genomeSize;
    private long? totUngappedLength;
    private int? gapsBtwnScaffolds;
    private int? numChromosomes;
    private int? numOrganelles;
    private int? numScaffolds;
    private long? scaffoldN50;
    private int? scaffoldL50;
    private int? numContigs;
    private long? contigN50;
    private int? contigL50;
    private decimal? gcPercent;
    private string? genomeProvider;
    private string? genomeName;
    private string? genomeDate;



    public string Accession
    {
        get { return accession; }
        set { accession = value; }
    }
    public string ScientificName
    {
        get { return scientificName; }
        set { scientificName = value; }
    }
    public string CommonName
    {
        get { return commonName; }
        set { commonName = value; }
    }
    public string AssemblyName
    {
        get { return assemblyName; }
        set { assemblyName = value; }
    }

    public string AssemblyLevel
    {
        get { return assemblyLevel; }
        set { assemblyLevel = value; }
    }

    public long? GenomeSize
    {
        get { return genomeSize; }
        set { genomeSize = value; }
    }
    
    public long? TotalUngappedLength
    {
        get { return totUngappedLength; }
        set { totUngappedLength = value; }
    }

    public int? GapsBetweenScaffolds
    {
        get { return gapsBtwnScaffolds; }
        set { gapsBtwnScaffolds = value; }
    }

    public int? NumChromosomes
    {
        get { return numChromosomes; }
        set { numChromosomes = value; }
    }

    public int? NumOrganelles
    {
        get { return numOrganelles; }
        set { numOrganelles = value; }
    }

    public int? NumScaffolds
    {
        get { return numScaffolds; }
        set { numScaffolds = value; }
    }

    public long? ScaffoldN50
    {
        get { return scaffoldN50; }
        set { scaffoldN50 = value; }
    }

    public int? ScaffoldL50
    {
        get { return scaffoldL50; }
        set { scaffoldL50 = value; }
    }

    public int? NumContigs
    {
        get { return numContigs; }
        set { numContigs = value; }
    }

    public long? ContigN50
    {
        get { return contigN50; }
        set { contigN50 = value; }
    }

    public int? ContigL50
    {
        get { return contigL50; }
        set { contigL50 = value; }
    }

    public decimal? GCPercent
    {
        get { return gcPercent; }
        set { gcPercent = value; }
    }

    public string? GenomeProvider
    {
        get { return genomeProvider; }
        set { genomeProvider = value; }
    }

    public string? GenomeName
    {
        get { return genomeName; }
        set { genomeName = value; }
    }

    public string? GenomeDate
    {
        get { return genomeDate; }
        set { genomeDate = value; }
    }
}