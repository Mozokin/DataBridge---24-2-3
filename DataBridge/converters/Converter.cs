using System.Globalization;
namespace DataBridge;
public interface IConverter { DataSet Convert(DataSet source,Config config); }
public sealed class Converter : IConverter
{
    public DataSet Convert(DataSet source,Config config)
    {
        string Map(string name)=>config.Mapping.GetValueOrDefault(name,name);
        var columns=source.Columns.Select(Map).ToList();if(columns.Distinct().Count()!=columns.Count)throw new InvalidDataException("Маппинг создаёт одинаковые имена столбцов.");
        var rows=new List<Record>();foreach(var input in source.Rows){var transformed=new Record(input);
            foreach(var t in config.Transformations){if(!transformed.TryGetValue(t.Field,out var raw)||Scalar.Empty(raw))continue;transformed[t.Field]=Apply(t,raw);}
            var row=new Record();foreach(var pair in transformed)row.Add(Map(pair.Key),pair.Value);rows.Add(row);
        }return new(columns,rows);
    }
    public static object Apply(TransformRule t,object? raw){string text=Scalar.Text(raw);return t.Operation switch{"trim"=>text.Trim(),"upper"=>text.ToUpperInvariant(),"lower"=>text.ToLowerInvariant(),"replace"=>t.Values.GetValueOrDefault(text,text),"timestamp_to_date"=>Timestamp(text),_=>throw new InvalidDataException($"Неизвестная трансформация {t.Operation}.")};}
    static string Timestamp(string text){if(!long.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out long n))throw new InvalidDataException($"Некорректный Unix timestamp: {text}.");try{return DateTimeOffset.FromUnixTimeSeconds(n).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);}catch(ArgumentOutOfRangeException){throw new InvalidDataException($"Unix timestamp вне диапазона: {text}.");}}
}
