using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
namespace DataBridge;
public sealed class Record : Dictionary<string, object?>
{
    public Record() : base(StringComparer.Ordinal) { }
    public Record(IDictionary<string, object?> source) : base(source, StringComparer.Ordinal) { }
}
public sealed class DataSet
{
    public List<string> Columns { get; }
    public List<Record> Rows { get; }
    public DataSet(IEnumerable<string> columns, IEnumerable<Record> rows) { Columns = columns.Distinct(StringComparer.Ordinal).ToList(); Rows = rows.ToList(); }
    public static DataSet FromRows(IEnumerable<Record> records) { var rows = records.ToList(); return new(rows.SelectMany(r => r.Keys), rows); }
}
public static class Scalar
{
    public static string Text(object? value) => value switch { null => "", bool b => b ? "true" : "false", IFormattable f => f.ToString(null, CultureInfo.InvariantCulture), _ => value.ToString() ?? "" };
    public static bool Empty(object? value) => value is null || string.IsNullOrWhiteSpace(Text(value));
    public static object? Json(JsonElement e) => e.ValueKind switch { JsonValueKind.Null => null, JsonValueKind.String => e.GetString(), JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.Number => e.TryGetInt64(out var i) ? (object)i : e.TryGetDecimal(out var d) ? (object)d : e.GetDouble(), _ => throw new InvalidDataException("Поддерживаются только плоские записи: вложенные объекты и массивы недопустимы.") };
}
public sealed class Config
{
    public List<FieldRule> Fields { get; set; } = new();
    public CsvOptions Csv { get; set; } = new();
    public Dictionary<string,string> Mapping { get; set; } = new();
    public List<TransformRule> Transformations { get; set; } = new();
    public static Config Load(string? path)
    {
        var c = path is null ? new Config() : JsonSerializer.Deserialize<Config>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Пустая конфигурация.");
        if (c.Fields is null || c.Csv is null || c.Mapping is null || c.Transformations is null) throw new InvalidDataException("Разделы конфигурации не должны быть null.");
        if (c.Csv.Delimiter is null || c.Csv.Delimiter.Length != 1 || "\r\n\"".Contains(c.Csv.Delimiter[0])) throw new InvalidDataException("CSV: разделитель должен быть одним символом, кроме кавычки и переноса строки.");
        if (c.Csv.Columns is null) throw new InvalidDataException("csv.columns не должен быть null.");
        if (c.Csv.Columns.Any(string.IsNullOrWhiteSpace) || c.Csv.Columns.Distinct().Count() != c.Csv.Columns.Count) throw new InvalidDataException("Имена csv.columns должны быть непустыми и уникальными.");
        if (c.Fields.Any(f => f is null || string.IsNullOrWhiteSpace(f.Name)) || c.Fields.Select(f => f.Name).Distinct().Count() != c.Fields.Count) throw new InvalidDataException("Имена полей должны быть непустыми и уникальными.");
        foreach (var f in c.Fields) f.Check();
        if (c.Mapping.Any(m => string.IsNullOrWhiteSpace(m.Key) || string.IsNullOrWhiteSpace(m.Value))) throw new InvalidDataException("Маппинг: пустое имя поля.");
        foreach (var t in c.Transformations) if (t is null || string.IsNullOrWhiteSpace(t.Field) || t.Values is null || t.Operation is not ("trim" or "upper" or "lower" or "replace" or "timestamp_to_date")) throw new InvalidDataException("Неизвестная или неполная трансформация.");
        return c;
    }
}
public sealed class CsvOptions { public string Delimiter { get; set; } = ","; public bool HasHeader { get; set; } = true; public List<string> Columns { get; set; } = new(); }
public sealed class TransformRule { public string Field { get; set; } = ""; public string Operation { get; set; } = ""; public Dictionary<string,string> Values { get; set; } = new(); }
public sealed class FieldRule
{
    public string Name { get; set; } = ""; public string Type { get; set; } = "string"; public bool Required { get; set; } public bool Unique { get; set; }
    public JsonElement? Min { get; set; } public JsonElement? Max { get; set; } public string? Pattern { get; set; } public int? MinLength { get; set; } public int? MaxLength { get; set; } public string? Format { get; set; }
    public string DateFormat => (Format ?? "yyyy-MM-dd").Replace("YYYY", "yyyy").Replace("DD", "dd");
    public void Check()
    {
        if (Type is not ("integer" or "float" or "string" or "date" or "boolean")) throw new InvalidDataException($"Поле {Name}: неизвестный тип {Type}.");
        if (MinLength < 0 || MaxLength < 0 || (MinLength.HasValue && MaxLength.HasValue && MinLength > MaxLength)) throw new InvalidDataException($"Поле {Name}: некорректные границы длины.");
        if (Pattern is not null) _ = new Regex(Pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if ((Min.HasValue || Max.HasValue) && Type is not ("integer" or "float" or "date")) throw new InvalidDataException($"Поле {Name}: min/max только для чисел и дат.");
        if (Type == "date") _ = DateTime.Today.ToString(DateFormat, CultureInfo.InvariantCulture);
        if (Min.HasValue && !TryValue(Scalar.Text(Scalar.Json(Min.Value)), out _)) throw new InvalidDataException($"Поле {Name}: некорректный min.");
        if (Max.HasValue && !TryValue(Scalar.Text(Scalar.Json(Max.Value)), out _)) throw new InvalidDataException($"Поле {Name}: некорректный max.");
        if (Min.HasValue && Max.HasValue && CompareBound(Min.Value, Max.Value) > 0) throw new InvalidDataException($"Поле {Name}: min больше max.");
    }
    private int CompareBound(JsonElement a, JsonElement b) { TryValue(Scalar.Text(Scalar.Json(a)), out var av); TryValue(Scalar.Text(Scalar.Json(b)), out var bv); return ((IComparable)av!).CompareTo(bv); }
    public bool TryValue(object? raw, out object? value)
    {
        string text = Scalar.Text(raw); value = null;
        switch (Type)
        {
            case "string": if (raw is not string) return false; value = text; return true;
            case "integer": if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long i)) { value = i; return true; } return false;
            case "float": if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal d)) { value = d; return true; } return false;
            case "date": if (DateTime.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date)) { value = date; return true; } return false;
            case "boolean": if (bool.TryParse(text, out bool b)) { value = b; return true; } if (text is "1" or "0") { value = text == "1"; return true; } return false;
            default: return false;
        }
    }
    public int Compare(object value, JsonElement bound) { TryValue(Scalar.Text(Scalar.Json(bound)), out var other); return ((IComparable)value).CompareTo(other); }
}
