using System.Globalization;
using System.Text;
using Microsoft.VisualBasic.FileIO;
using TheaterInvitations.Web.Data;

namespace TheaterInvitations.Web.Services;

public sealed class CsvImportParser
{
    public const int DefaultMaximumBytes = 1_000_000;
    private static readonly string[] RecognizedHeaders = ["primary_guest_name", "email", "allocated_seats", "company", "priority", "phone"];

    public CsvImportDocument Parse(Stream input, int maximumBytes = DefaultMaximumBytes)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = input.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > maximumBytes) return CsvImportDocument.Failure("CSV nesmí být větší než 1 MB.");
            buffer.Write(chunk, 0, read);
        }
        if (buffer.Length > maximumBytes) return CsvImportDocument.Failure("CSV nesmí být větší než 1 MB.");

        string text;
        try { text = new UTF8Encoding(false, true).GetString(buffer.ToArray()).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException) { return CsvImportDocument.Failure("CSV musí být platné UTF-8."); }
        if (string.IsNullOrWhiteSpace(text)) return CsvImportDocument.Failure("CSV je prázdné.");

        IReadOnlyList<string[]> rows;
        try
        {
            using var parser = new TextFieldParser(new StringReader(text)) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
            parser.SetDelimiters(",");
            var parsed = new List<string[]>();
            while (!parser.EndOfData) parsed.Add(parser.ReadFields() ?? []);
            rows = parsed;
        }
        catch (MalformedLineException exception) { return CsvImportDocument.Failure($"CSV je chybně formátované poblíž řádku {exception.LineNumber}."); }

        if (rows.Count == 0) return CsvImportDocument.Failure("CSV je prázdné.");
        var header = rows[0];
        var documentFindings = new List<string>();
        var ignored = new List<string>();
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < header.Length; index++)
        {
            var name = header[index];
            if (!RecognizedHeaders.Contains(name, StringComparer.Ordinal)) { ignored.Add(name); continue; }
            if (!indexes.TryAdd(name, index)) documentFindings.Add($"Duplicitní sloupec: {name}.");
        }
        foreach (var required in new[] { "primary_guest_name", "email", "allocated_seats" })
            if (!indexes.ContainsKey(required)) documentFindings.Add($"Chybí povinný sloupec: {required}.");

        var resultRows = new List<CsvImportRow>();
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalSeats = 0;
        for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
        {
            var fields = rows[rowIndex];
            var findings = new List<string>();
            var values = new string?[RecognizedHeaders.Length];
            foreach (var pair in indexes) values[Array.IndexOf(RecognizedHeaders, pair.Key)] = pair.Value < fields.Length ? fields[pair.Value] : null;
            if (fields.Length != header.Length) findings.Add("Řádek nemá stejný počet sloupců jako hlavička.");

            string? name = null, email = null, company = null, phone = null;
            int? seats = null, priority = null;
            try { name = PartyDataValidation.NormalizeName(values[0]); } catch (ArgumentException exception) { findings.Add(exception.Message); }
            try { email = PartyEmailValidation.Normalize(values[1] ?? string.Empty); } catch (ArgumentException) { findings.Add("E-mail musí být platná adresa."); }
            try { seats = PartyDataValidation.NormalizeSeats(values[2]); } catch (ArgumentException exception) { findings.Add(exception.Message); }
            try { company = PartyDataValidation.NormalizeCompany(values[3]); } catch (ArgumentException exception) { findings.Add(exception.Message); }
            try { priority = PartyDataValidation.NormalizePriority(values[4]); } catch (ArgumentException exception) { findings.Add(exception.Message); }
            try { phone = PartyDataValidation.NormalizePhone(values[5]); } catch (ArgumentException exception) { findings.Add(exception.Message); }
            if (email is not null && !seenEmails.Add(email)) findings.Add("E-mail je v tomto souboru duplicitní.");
            if (seats is not null && totalSeats > int.MaxValue - seats.Value) findings.Add("Součet počtu míst je příliš vysoký.");
            else if (seats is not null) totalSeats += seats.Value;
            resultRows.Add(new CsvImportRow(rowIndex + 1, name, email, seats, company, priority ?? 3, phone, findings));
        }
        return new CsvImportDocument(documentFindings, ignored, resultRows, totalSeats > int.MaxValue ? null : (int)totalSeats);
    }
}

public sealed record CsvImportDocument(IReadOnlyList<string> DocumentFindings, IReadOnlyList<string> IgnoredHeaders, IReadOnlyList<CsvImportRow> Rows, int? AllocatedSeatTotal)
{
    public bool IsValid => DocumentFindings.Count == 0 && Rows.Count > 0 && Rows.All(row => row.Findings.Count == 0);
    public static CsvImportDocument Failure(string finding) => new([finding], [], [], null);
}

public sealed record CsvImportRow(int SourceRowNumber, string? Name, string? Email, int? AllocatedSeats, string? Company, int Priority, string? Phone, IReadOnlyList<string> Findings)
{
    public string? ValidationIssue => Findings.Count == 0 ? null : string.Join(" ", Findings);
}
