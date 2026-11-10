using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

public static class MarkdigMarkdownTableParser
{
    public static string MarkdownTableToJson(string markdown)
    {
        // 1. Parse markdown to AST
        var document = Markdown.Parse(markdown);

        // 2. Find the first table in the document
        var table = document.Descendants<Table>().FirstOrDefault();
        if (table == null)
            throw new InvalidOperationException("No markdown table found in the input.");

        // 3. Extract header row (row.IsHeader == true)
        var headerRow = table
            .Descendants<TableRow>()
            .FirstOrDefault(r => r.IsHeader);

        if (headerRow == null)
            throw new InvalidOperationException("No header row found in the table.");

        var headers = headerRow
            .Descendants<TableCell>()
            .Select(GetCellText)
            .ToList();

        // 4. Extract body rows (row.IsHeader == false)
        var bodyRows = table
            .Descendants<TableRow>()
            .Where(r => !r.IsHeader)
            .ToList();

        var records = new List<Dictionary<string, string>>();

        foreach (var row in bodyRows)
        {
            var cells = row.Descendants<TableCell>().ToList();
            var record = new Dictionary<string, string>();

            for (int i = 0; i < headers.Count && i < cells.Count; i++)
            {
                var header = headers[i];
                var value = GetCellText(cells[i]);
                record[header] = value;
            }

            records.Add(record);
        }

        // 5. Serialize to JSON
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        return JsonSerializer.Serialize(records, options);
    }

    /// <summary>
    /// Extracts plain text from a TableCell by walking its inline literals.
    /// </summary>
    private static string GetCellText(TableCell cell)
    {
        var sb = new StringBuilder();

        foreach (var block in cell)
        {
            if (block is ParagraphBlock paragraph && paragraph.Inline != null)
            {
                foreach (var inline in paragraph.Inline)
                {
                    if (inline is LiteralInline literal)
                    {
                        sb.Append(literal.Content.ToString());
                    }
                }
            }
        }

        return sb.ToString().Trim();
    }
}
