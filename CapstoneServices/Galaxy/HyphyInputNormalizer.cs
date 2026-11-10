using System.Text;
using System.Text.RegularExpressions;

namespace CapstoneServices.Galaxy;

public static class HyphyInputNormalizer
{
    public sealed record NormalizedInputs(string Fasta, string Newick, IReadOnlyList<string> Identifiers);

    private sealed record FastaRecord(string OriginalHeader, string PrimaryId, string Sequence);

    public static NormalizedInputs NormalizeAndValidate(string fasta, string newick)
    {
        var records = ParseAndValidateFasta(fasta);
        var discoveryParser = new NewickParser(newick, label => label);
        discoveryParser.Parse();
        var treeLeaves = discoveryParser.LeafIdentifiers;
        if (treeLeaves.Count != records.Count)
            throw new InvalidOperationException(
                $"FASTA and Newick taxa do not match: the alignment has {records.Count} sequences, " +
                $"but the tree has {treeLeaves.Count} leaves.");

        var recordToLeaf = MatchFastaRecordsToTreeLeaves(records, treeLeaves);
        var safeTreeLabels = treeLeaves.ToDictionary(
            leaf => leaf,
            NormalizeIdentifier,
            StringComparer.Ordinal);
        var collision = safeTreeLabels
            .GroupBy(item => item.Value, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (collision is not null)
            throw new InvalidOperationException(
                $"Identifier normalization collision: {string.Join(", ", collision.Select(item => item.Key))} " +
                $"all become '{collision.Key}'.");

        var normalizedTree = new NewickParser(newick, label => safeTreeLabels[label]).Parse();
        var normalizedIds = records
            .Select((_, index) => safeTreeLabels[recordToLeaf[index]])
            .ToList();
        var normalizedFasta = string.Join('\n', records.Select((record, index) =>
            $">{normalizedIds[index]}\n{record.Sequence}")) + "\n";
        return new NormalizedInputs(normalizedFasta, normalizedTree, normalizedIds);
    }

    private static Dictionary<int, string> MatchFastaRecordsToTreeLeaves(
        IReadOnlyList<FastaRecord> records,
        IReadOnlyList<string> treeLeaves)
    {
        var candidates = (
            from recordIndex in Enumerable.Range(0, records.Count)
            from leafIndex in Enumerable.Range(0, treeLeaves.Count)
            let score = IdentifierMatchScore(records[recordIndex], treeLeaves[leafIndex])
            where score > 0
            select (RecordIndex: recordIndex, LeafIndex: leafIndex, Score: score))
            .ToList();
        var assignedRecords = new HashSet<int>();
        var assignedLeaves = new HashSet<int>();
        var result = new Dictionary<int, string>();

        while (assignedRecords.Count < records.Count)
        {
            var remaining = candidates
                .Where(candidate => !assignedRecords.Contains(candidate.RecordIndex) &&
                                    !assignedLeaves.Contains(candidate.LeafIndex))
                .ToList();
            var deterministic = remaining
                .Where(candidate =>
                {
                    var recordBest = remaining
                        .Where(item => item.RecordIndex == candidate.RecordIndex)
                        .Max(item => item.Score);
                    var leafBest = remaining
                        .Where(item => item.LeafIndex == candidate.LeafIndex)
                        .Max(item => item.Score);
                    return candidate.Score == recordBest &&
                           candidate.Score == leafBest &&
                           remaining.Count(item => item.RecordIndex == candidate.RecordIndex && item.Score == recordBest) == 1 &&
                           remaining.Count(item => item.LeafIndex == candidate.LeafIndex && item.Score == leafBest) == 1;
                })
                .OrderByDescending(candidate => candidate.Score)
                .ToList();
            if (deterministic.Count == 0)
                break;

            foreach (var candidate in deterministic)
            {
                if (assignedRecords.Contains(candidate.RecordIndex) || assignedLeaves.Contains(candidate.LeafIndex))
                    continue;
                assignedRecords.Add(candidate.RecordIndex);
                assignedLeaves.Add(candidate.LeafIndex);
                result[candidate.RecordIndex] = treeLeaves[candidate.LeafIndex];
            }
        }

        if (result.Count != records.Count)
        {
            var unmatchedHeaders = Enumerable.Range(0, records.Count)
                .Where(index => !result.ContainsKey(index))
                .Select(index => records[index].PrimaryId)
                .ToList();
            var unmatchedLeaves = Enumerable.Range(0, treeLeaves.Count)
                .Where(index => !assignedLeaves.Contains(index))
                .Select(index => treeLeaves[index])
                .ToList();
            throw new InvalidOperationException(
                $"FASTA and Newick taxa do not match. Alignment headers that could not be mapped: " +
                $"{FormatList(unmatchedHeaders)}. Tree leaves not present in the alignment: {FormatList(unmatchedLeaves)}.");
        }

        return result;
    }

    private static int IdentifierMatchScore(FastaRecord record, string treeLeaf)
    {
        if (record.PrimaryId.Equals(treeLeaf, StringComparison.OrdinalIgnoreCase))
            return 100_000;

        var primary = NormalizeIdentifier(record.PrimaryId);
        var leaf = NormalizeIdentifier(treeLeaf);
        if (primary.Equals(leaf, StringComparison.OrdinalIgnoreCase))
            return 90_000;

        var recordAliases = IdentifierAliases(record.OriginalHeader);
        var leafAliases = IdentifierAliases(treeLeaf);
        var shared = recordAliases.Intersect(leafAliases, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(alias => alias.Length)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(shared))
            return 50_000 + shared.Length;

        if (primary.Length >= 6 && leaf.Length >= 6 &&
            (primary.Contains(leaf, StringComparison.OrdinalIgnoreCase) ||
             leaf.Contains(primary, StringComparison.OrdinalIgnoreCase)))
            return 10_000 + Math.Min(primary.Length, leaf.Length);

        return 0;
    }

    private static HashSet<string> IdentifierAliases(string value)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return;
            var normalized = NormalizeIdentifier(candidate).ToLowerInvariant();
            if (normalized.Length < 3 || normalized is "gene" or "protein" or "protein_id" or "locus_tag")
                return;
            aliases.Add(normalized);
            var versionless = Regex.Replace(normalized, @"(?<=\d)_\d+$", string.Empty);
            if (versionless.Length >= 3)
                aliases.Add(versionless);
        }

        Add(value);
        foreach (Match match in Regex.Matches(value, @"[A-Za-z]{1,12}_?\d+(?:\.\d+)?"))
            Add(match.Value);
        foreach (var token in Regex.Split(value, @"[^A-Za-z0-9_.-]+"))
            Add(token);
        return aliases;
    }

    public static string NormalizeIdentifier(string identifier)
    {
        var primary = (identifier ?? string.Empty).Trim();
        if (primary.Length == 0)
            throw new InvalidOperationException("An empty sequence or tree identifier was encountered.");

        var normalized = Regex.Replace(primary, "[^A-Za-z0-9_-]", "_");
        normalized = Regex.Replace(normalized, "_+", "_").Trim('_');
        if (normalized.Length == 0)
            throw new InvalidOperationException($"Identifier '{identifier}' contains no usable characters.");
        return normalized;
    }

    private static List<FastaRecord> ParseAndValidateFasta(string fasta)
    {
        if (string.IsNullOrWhiteSpace(fasta))
            throw new InvalidOperationException("The codon alignment is empty.");

        var records = new List<FastaRecord>();
        string? originalHeader = null;
        var sequence = new StringBuilder();

        void FinishRecord()
        {
            if (originalHeader is null) return;
            var primaryId = originalHeader.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).First();
            var bases = sequence.ToString();
            if (bases.Length == 0)
                throw new InvalidOperationException($"Sequence '{primaryId}' is empty.");
            records.Add(new FastaRecord(originalHeader, primaryId, bases));
            sequence.Clear();
        }

        foreach (var rawLine in fasta.Replace("\r", string.Empty).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('>'))
            {
                FinishRecord();
                var header = line[1..].Trim();
                originalHeader = header;
                if (string.IsNullOrWhiteSpace(originalHeader))
                    throw new InvalidOperationException("A FASTA record has no primary identifier.");
                continue;
            }
            if (originalHeader is null)
                throw new InvalidOperationException("The codon alignment contains sequence data before its first FASTA header.");
            sequence.Append(Regex.Replace(line, "\\s+", string.Empty));
        }
        FinishRecord();

        if (records.Count < 3)
            throw new InvalidOperationException("HyPhy requires at least three aligned sequences.");

        var duplicateOriginal = records.GroupBy(record => record.PrimaryId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateOriginal is not null)
            throw new InvalidOperationException($"Duplicate FASTA identifier '{duplicateOriginal.Key}'.");

        var length = records[0].Sequence.Length;
        foreach (var record in records)
        {
            var validationSequence = record.Sequence.ToUpperInvariant();
            if (record.Sequence.Length != length)
                throw new InvalidOperationException(
                    $"Codon alignment lengths differ: '{record.PrimaryId}' has {record.Sequence.Length} bases; expected {length}.");
            if (record.Sequence.Length % 3 != 0)
                throw new InvalidOperationException(
                    $"Sequence '{record.PrimaryId}' has length {record.Sequence.Length}, which is not divisible by three.");
            var invalid = validationSequence.FirstOrDefault(value => !"ACGTRYSWKMBDHVN-?".Contains(value));
            if (invalid != default)
                throw new InvalidOperationException($"Sequence '{record.PrimaryId}' contains invalid nucleotide character '{invalid}'.");

            for (var offset = 0; offset < validationSequence.Length; offset += 3)
            {
                var codon = validationSequence.Substring(offset, 3);
                if (codon.Contains('-') && codon != "---")
                    throw new InvalidOperationException(
                        $"Sequence '{record.PrimaryId}' contains a partial codon gap at codon {offset / 3 + 1}.");
                if (offset < validationSequence.Length - 3 && codon is ("TAA" or "TAG" or "TGA"))
                    throw new InvalidOperationException(
                        $"Sequence '{record.PrimaryId}' contains internal stop codon {codon} at codon {offset / 3 + 1}.");
            }
        }

        return records;
    }

    private static string FormatList(IReadOnlyCollection<string> values) =>
        values.Count == 0 ? "none" : string.Join(", ", values.Take(8)) + (values.Count > 8 ? ", ..." : string.Empty);

    private sealed class NewickParser
    {
        private readonly string _source;
        private readonly Func<string, string> _leafMapper;
        private readonly StringBuilder _output = new();
        private int _position;

        public NewickParser(string source, Func<string, string> leafMapper)
        {
            _source = (source ?? string.Empty).Trim();
            _leafMapper = leafMapper;
        }

        public List<string> LeafIdentifiers { get; } = new();

        public string Parse()
        {
            if (_source.Length == 0)
                throw new InvalidOperationException("The Newick tree is empty.");
            ParseSubtree();
            SkipWhitespace();
            if (_position >= _source.Length || _source[_position] != ';')
                throw Error("The Newick tree must end with a semicolon.");
            _output.Append(';');
            _position++;
            SkipWhitespace();
            if (_position != _source.Length)
                throw Error("Unexpected text after the Newick semicolon.");
            return _output.ToString();
        }

        private void ParseSubtree()
        {
            SkipWhitespace();
            if (_position < _source.Length && _source[_position] == '(')
            {
                Copy('(');
                ParseSubtree();
                while (true)
                {
                    SkipWhitespace();
                    if (_position < _source.Length && _source[_position] == ',')
                    {
                        Copy(',');
                        ParseSubtree();
                        continue;
                    }
                    break;
                }
                SkipWhitespace();
                if (_position >= _source.Length || _source[_position] != ')')
                    throw Error("Expected ')' after child nodes.");
                Copy(')');
                CopyNodeSuffix();
                return;
            }

            var label = ReadLabel();
            var normalized = _leafMapper(label);
            if (LeafIdentifiers.Contains(normalized, StringComparer.Ordinal))
                throw Error($"Tree contains duplicate leaf identifier '{normalized}'.");
            LeafIdentifiers.Add(normalized);
            _output.Append(normalized);
            CopyNodeSuffix();
        }

        private string ReadLabel()
        {
            SkipWhitespace();
            if (_position >= _source.Length)
                throw Error("Expected a leaf label.");
            if (_source[_position] == '\'')
            {
                _position++;
                var value = new StringBuilder();
                while (_position < _source.Length)
                {
                    var current = _source[_position++];
                    if (current != '\'')
                    {
                        value.Append(current);
                        continue;
                    }
                    if (_position < _source.Length && _source[_position] == '\'')
                    {
                        value.Append('\'');
                        _position++;
                        continue;
                    }
                    return value.ToString();
                }
                throw Error("Unterminated quoted Newick label.");
            }

            var start = _position;
            while (_position < _source.Length && !":,();[]{}".Contains(_source[_position]) && !char.IsWhiteSpace(_source[_position]))
                _position++;
            if (_position == start)
                throw Error("Expected a non-empty leaf label.");
            return _source[start.._position];
        }

        private void CopyNodeSuffix()
        {
            var squareDepth = 0;
            var curlyDepth = 0;
            var quoted = false;
            while (_position < _source.Length)
            {
                var current = _source[_position];
                if (current == '\'' && squareDepth == 0)
                    quoted = !quoted;
                if (!quoted)
                {
                    if (current == '[') squareDepth++;
                    else if (current == ']') squareDepth--;
                    else if (current == '{') curlyDepth++;
                    else if (current == '}') curlyDepth--;
                    else if (squareDepth == 0 && curlyDepth == 0 && current is ',' or ')' or ';') break;
                }
                _output.Append(current);
                _position++;
            }
            if (squareDepth != 0 || curlyDepth != 0 || quoted)
                throw Error("Unterminated Newick annotation or quoted label.");
        }

        private void SkipWhitespace()
        {
            while (_position < _source.Length && char.IsWhiteSpace(_source[_position]))
                _position++;
        }

        private void Copy(char expected)
        {
            if (_position >= _source.Length || _source[_position] != expected)
                throw Error($"Expected '{expected}'.");
            _output.Append(expected);
            _position++;
        }

        private InvalidOperationException Error(string message) =>
            new($"Invalid Newick near character {_position + 1}: {message}");
    }
}
