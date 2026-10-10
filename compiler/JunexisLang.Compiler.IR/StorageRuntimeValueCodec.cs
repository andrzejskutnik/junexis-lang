using System.Globalization;
using System.Text.Json;

namespace JunexisLang.Compiler.IR;

/// <summary>Wspólna kanonizacja przenośnych wartości Query i Mutation Runtime.</summary>
internal static class StorageRuntimeValueCodec
{
    internal static string TypeName(StorageValueType type) => type switch
    {
        StorageValueType.Int64 => "int64",
        StorageValueType.Float64 => "float64",
        StorageValueType.Bool => "bool",
        StorageValueType.Text => "text",
        StorageValueType.Bytes => "bytes",
        StorageValueType.Decimal => "decimal",
        StorageValueType.Timestamp => "timestamp",
        StorageValueType.Uuid => "uuid",
        StorageValueType.Json => "json",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    internal static bool TryNormalize(StorageValueType type, string value, out string normalized)
    {
        normalized = string.Empty;
        switch (type)
        {
            case StorageValueType.Int64 when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer):
                normalized = integer.ToString(CultureInfo.InvariantCulture);
                return true;
            case StorageValueType.Float64 when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                                               && double.IsFinite(number):
                normalized = number.ToString("R", CultureInfo.InvariantCulture);
                return true;
            case StorageValueType.Bool when bool.TryParse(value, out var boolean):
                normalized = boolean ? "true" : "false";
                return true;
            case StorageValueType.Text:
                normalized = value;
                return true;
            case StorageValueType.Bytes:
                try
                {
                    normalized = Convert.ToBase64String(Convert.FromBase64String(value));
                    return true;
                }
                catch (FormatException)
                {
                    return false;
                }
            case StorageValueType.Decimal when decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue):
                normalized = decimalValue.ToString("G29", CultureInfo.InvariantCulture);
                return true;
            case StorageValueType.Timestamp when DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var timestamp):
                normalized = timestamp.ToString("O", CultureInfo.InvariantCulture);
                return true;
            case StorageValueType.Uuid when Guid.TryParse(value, out var uuid):
                normalized = uuid.ToString("D");
                return true;
            case StorageValueType.Json:
                try
                {
                    using var document = JsonDocument.Parse(value);
                    normalized = JsonSerializer.Serialize(document.RootElement);
                    return true;
                }
                catch (JsonException)
                {
                    return false;
                }
            default:
                return false;
        }
    }
}
