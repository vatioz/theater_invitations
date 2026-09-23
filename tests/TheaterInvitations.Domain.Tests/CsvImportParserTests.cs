using System.Text;
using TheaterInvitations.Web.Services;

namespace TheaterInvitations.Domain.Tests;

public sealed class CsvImportParserTests
{
    [Fact]
    public void Parses_optional_columns_in_any_order_and_defaults_priority()
    {
        var document = Parse("phone,email,primary_guest_name,allocated_seats,unknown,company\n+420 123, Guest@Example.test ,\"Dvořák, Jana\",2,ignored,Divadlo");

        var row = Assert.Single(document.Rows);
        Assert.True(document.IsValid);
        Assert.Equal("Guest@Example.test", row.Email);
        Assert.Equal("Dvořák, Jana", row.Name);
        Assert.Equal(3, row.Priority);
        Assert.Equal("+420 123", row.Phone);
        Assert.Equal("unknown", Assert.Single(document.IgnoredHeaders));
    }

    [Fact]
    public void Parses_the_current_czech_export_headers()
    {
        var document = Parse("jméno a příjmení,e-mail,telefon,priorita,kdo pozval,doprovod,společnost,pozice\nMichal Geher,michal.geher@gehergeo.cz,724 301 700,1,GEHER GEO s.r.o.,2,,");

        var row = Assert.Single(document.Rows);
        Assert.True(document.IsValid);
        Assert.Equal("Michal Geher", row.Name);
        Assert.Equal("michal.geher@gehergeo.cz", row.Email);
        Assert.Equal(2, row.AllocatedSeats);
        Assert.Equal(1, row.Priority);
        Assert.Equal("724 301 700", row.Phone);
        Assert.Contains("kdo pozval", document.IgnoredHeaders);
        Assert.Contains("pozice", document.IgnoredHeaders);
    }

    [Fact]
    public void Reports_all_row_findings_and_duplicate_headers()
    {
        var document = Parse("primary_guest_name,email,email,allocated_seats,priority,phone\n,not-email,other,0,4,\"bad\u0001phone\"");

        Assert.Contains(document.DocumentFindings, finding => finding.Contains("Duplicitní"));
        Assert.True(Assert.Single(document.Rows).Findings.Count >= 4);
    }

    [Fact]
    public void Accepts_bom_multiline_fields_and_rejects_invalid_utf8_and_oversized_input()
    {
        var valid = ParseBytes(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("primary_guest_name,email,allocated_seats\n\"Řádek\ntext\",a@example.test,1")).ToArray());
        Assert.True(valid.IsValid);

        var invalid = new CsvImportParser().Parse(new MemoryStream([0xFF, 0xFE]));
        Assert.Contains(invalid.DocumentFindings, finding => finding.Contains("UTF-8"));

        var oversized = new CsvImportParser().Parse(new MemoryStream(Encoding.UTF8.GetBytes("12345")), 4);
        Assert.Contains(oversized.DocumentFindings, finding => finding.Contains("1 MB"));
    }

    private static CsvImportDocument Parse(string csv) => ParseBytes(Encoding.UTF8.GetBytes(csv));
    private static CsvImportDocument ParseBytes(byte[] bytes) => new CsvImportParser().Parse(new MemoryStream(bytes));
}
