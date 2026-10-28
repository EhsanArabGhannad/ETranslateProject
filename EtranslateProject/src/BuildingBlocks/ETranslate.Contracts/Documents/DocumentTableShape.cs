using System.Text.Json;

namespace ETranslate.Contracts.Documents;

public static class DocumentTableShape
{
    // Call only after validating every cell's attributes.
    public static int Columns(JsonElement row) => row.GetProperty("content").EnumerateArray().Sum(cell =>
        cell.TryGetProperty("attrs", out var attrs) && attrs.TryGetProperty("colspan", out var span) ? (int)span.GetDecimal() : 1);
}
