using CapstoneServices.NCBI;

using Path = CapstoneServices.NCBI.NcbiClient.TaxonomyPath;

var passed = 0;
void Check(string name, bool condition)
{
    if (!condition) throw new Exception($"FAILED: {name}");
    Console.WriteLine($"PASS: {name}");
    passed++;
}

Path P(string name, string taxId, params string[] lineage) => new(name, taxId, lineage.Append(taxId).ToArray());
HashSet<string> Leaves(string newick) => new(newick.Split(new[] { '(', ')', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
    .Select(value => value.Split(':')[0].Trim())
    .Where(value => value.Length > 0 && !value.All(char.IsDigit))
    .Select(value => value.Replace('_', ' ')));
bool HasClade(string newick, params string[] expected)
{
    var target = expected.ToHashSet();
    var stack = new Stack<HashSet<string>>();
    var token = "";
    var clades = new List<HashSet<string>>();
    void Flush()
    {
        var label = token.Trim().Split(':')[0].Replace('_', ' ');
        token = "";
        if (label.Length > 0 && stack.Count > 0) stack.Peek().Add(label);
    }
    foreach (var ch in newick)
    {
        if (ch == '(') stack.Push(new());
        else if (ch == ',') Flush();
        else if (ch == ')')
        {
            Flush();
            var completed = stack.Pop();
            clades.Add(new(completed));
            if (stack.Count > 0) stack.Peek().UnionWith(completed);
        }
        else if (ch != ';') token += ch;
    }
    return clades.Any(clade => clade.SetEquals(target));
}

var primates = new[] {
    P("Pan troglodytes", "9598", "1", "hominidae", "homininae", "hominini", "hominina", "pan"),
    P("Pan paniscus", "9597", "1", "hominidae", "homininae", "hominini", "hominina", "pan"),
    P("Homo sapiens", "9606", "1", "hominidae", "homininae", "hominini", "hominina", "homo"),
    P("Gorilla gorilla gorilla", "9595", "1", "hominidae", "homininae", "gorillini", "gorilla", "9593"),
    P("Pongo pygmaeus", "9600", "1", "hominidae", "ponginae", "pongo"),
    P("Pongo abelii", "9601", "1", "hominidae", "ponginae", "pongo")
};
var tree = NcbiClient.BuildReferenceTreeFromLineages(primates);
Console.WriteLine($"Six-primate Newick: {tree.Newick}");
Check("Pan species form a clade", HasClade(tree.Newick, "Pan troglodytes", "Pan paniscus"));
Check("Homo is sister within Homo+Pan clade", HasClade(tree.Newick, "Pan troglodytes", "Pan paniscus", "Homo sapiens"));
Check("Gorilla lies outside Homo+Pan", HasClade(tree.Newick, "Pan troglodytes", "Pan paniscus", "Homo sapiens", "Gorilla gorilla gorilla"));
Check("Pongo species form a clade", HasClade(tree.Newick, "Pongo pygmaeus", "Pongo abelii"));

var close = NcbiClient.BuildReferenceTreeFromLineages(primates.Take(2).ToArray());
Check("two close species", Leaves(close.Newick).Count == 2 && HasClade(close.Newick, "Pan troglodytes", "Pan paniscus"));
var genera = NcbiClient.BuildReferenceTreeFromLineages(new[] { primates[0], primates[2] });
Check("different genera", Leaves(genera.Newick).SetEquals(new[] { "Pan troglodytes", "Homo sapiens" }));
var orders = NcbiClient.BuildReferenceTreeFromLineages(new[] { P("Homo sapiens", "9606", "1", "mammalia", "primates"), P("Mus musculus", "10090", "1", "mammalia", "rodentia") });
Check("different mammalian orders", Leaves(orders.Newick).Count == 2 && orders.RootTaxId == "mammalia");
Check("adding a taxon preserves Pan clade", HasClade(NcbiClient.BuildReferenceTreeFromLineages(primates.Take(3).ToArray()).Newick, "Pan troglodytes", "Pan paniscus"));
Check("removing a taxon preserves remaining leaves", !Leaves(NcbiClient.BuildReferenceTreeFromLineages(primates.Where(p => p.TaxId != "9606").ToArray()).Newick).Contains("Homo sapiens"));
Check("duplicate TaxID is collapsed", NcbiClient.BuildReferenceTreeFromLineages(new[] { primates[0], primates[0], primates[1] }).ResolvedSpecies.Count == 2);
Check("invalid species is reported", NcbiClient.BuildReferenceTreeFromLineages(primates.Take(2).ToArray(), new[] { "invalid" }).MissingSpecies.SequenceEqual(new[] { "invalid" }));
var subspecies = NcbiClient.BuildReferenceTreeFromLineages(new[] { P("Gorilla gorilla", "9593", "1", "hominidae", "gorilla"), primates[3] });
Check("subspecies remains distinct from parent", Leaves(subspecies.Newick).SetEquals(new[] { "Gorilla gorilla", "Gorilla gorilla gorilla" }));
Check("fewer than two taxa returns no tree", string.IsNullOrEmpty(NcbiClient.BuildReferenceTreeFromLineages(primates.Take(1).ToArray()).Newick));
Check("missing reference taxon does not alter resolved tree", Leaves(NcbiClient.BuildReferenceTreeFromLineages(primates.Take(2).ToArray(), new[] { "missing" }).Newick).Count == 2);

Console.WriteLine($"{passed} topology tests passed.");
