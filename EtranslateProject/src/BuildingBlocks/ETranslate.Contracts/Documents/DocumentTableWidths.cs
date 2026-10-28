using System.Text.Json;

namespace ETranslate.Contracts.Documents;

public static class DocumentTableWidths
{
    public static bool IsValid(JsonElement value, int? columns = null)
    {
        if (value.ValueKind == JsonValueKind.Null) return true;
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() is < 1 or > 12 ||
            columns is not null && value.GetArrayLength() != columns) return false;
        decimal sum = 0;
        foreach (var width in value.EnumerateArray())
        {
            if (width.ValueKind != JsonValueKind.Number || !width.TryGetDecimal(out var number) ||
                number is < 1 or > 100 || decimal.Truncate(number) != number) return false;
            sum += number;
        }
        return sum == 100;
    }
}
