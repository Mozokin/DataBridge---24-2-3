using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
namespace DataBridge;
public interface IReader { DataSet Read(string path, Config config); }
public interface IWriter { void Write(string path, DataSet data, Config config); }
public static class Formats
{
    private static readonly Dictionary<string,(IReader Reader,IWriter Writer)> Registry = new(StringComparer.OrdinalIgnoreCase) { ["csv"] = (new CsvFormat(),new CsvFormat()), ["json"] = (new JsonFormat(),new JsonFormat()), ["xml"] = (new XmlFormat(),new XmlFormat()), ["xlsx"] = (new ExcelFormat(),new ExcelFormat()) };
    public static string Format(string path) => Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
    public static string Normalize(string format) => format.ToLowerInvariant() == "excel" ? "xlsx" : format.ToLowerInvariant();
    public static bool Supported(string format) => Registry.ContainsKey(Normalize(format));
    public static DataSet Read(string path, Config config) => Registry.TryGetValue(Format(path),out var f) ? f.Reader.Read(path,config) : throw new InvalidDataException($"Неподдерживаемый формат: {Path.GetExtension(path)}.");
    public static void Write(string path, string format, DataSet data, Config config) { if (!Registry.TryGetValue(Normalize(format),out var f)) throw new InvalidDataException($"Неподдерживаемый формат: {format}."); f.Writer.Write(path,data,config); }
}
public sealed class CsvFormat : IReader,IWriter
{
    public DataSet Read(string path, Config config)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, true); var records = Parse(reader,config.Csv.Delimiter[0]);
        if (records.Count == 0) return new(config.Csv.Columns.Count > 0 ? config.Csv.Columns : config.Fields.Select(f=>f.Name), Array.Empty<Record>());
        var cols = config.Csv.HasHeader ? records[0] : config.Csv.Columns.Count > 0 ? config.Csv.Columns : config.Fields.Count > 0 ? config.Fields.Select(f=>f.Name).ToList() : Enumerable.Range(1,records[0].Count).Select(i=>$"column{i}").ToList();
        if(cols.Any(string.IsNullOrWhiteSpace) || cols.Distinct().Count()!=cols.Count) throw new InvalidDataException("CSV: пустые или повторяющиеся заголовки.");
        var result = new List<Record>();
        for(int i=config.Csv.HasHeader?1:0;i<records.Count;i++) { var values=records[i]; if(values.Count!=cols.Count) throw new InvalidDataException($"CSV: запись {i+1}, ожидалось {cols.Count} столбцов, получено {values.Count}."); var row=new Record(); for(int j=0;j<cols.Count;j++) row[cols[j]]=values[j]; result.Add(row); }
        return new(cols,result);
    }
    public static List<List<string>> Parse(TextReader reader, char delimiter)
    {
        var all=new List<List<string>>(); var row=new List<string>(); var field=new StringBuilder(); bool quoted=false, closed=false, active=false;
        void EndField(){row.Add(field.ToString());field.Clear();closed=false;}
        void EndRow(){EndField();all.Add(row);row=new();active=false;}
        int n; while((n=reader.Read())!=-1){char c=(char)n; active=true;
            if(quoted){if(c=='"'){if(reader.Peek()=='"'){reader.Read();field.Append('"');}else{quoted=false;closed=true;}}else field.Append(c);continue;}
            if(c==delimiter){EndField();continue;}
            if(c=='\n'||c=='\r'){if(c=='\r'&&reader.Peek()=='\n')reader.Read();EndRow();continue;}
            if(closed)throw new InvalidDataException("CSV: неожиданный символ после закрывающей кавычки.");
            if(c=='"'){if(field.Length!=0)throw new InvalidDataException("CSV: кавычка внутри неэкранированного поля.");quoted=true;}else field.Append(c);
        }
        if(quoted)throw new InvalidDataException("CSV: незакрытая кавычка."); if(active||row.Count>0||field.Length>0)EndRow(); return all;
    }
    public void Write(string path, DataSet data, Config config) => WriteRows(path,data,config.Csv.Delimiter[0],config.Csv.HasHeader);
    public static void WriteRows(string path, DataSet data, char delimiter=',', bool header=true)
    {
        using var w=new StreamWriter(path,false,new UTF8Encoding(true)); string Escape(string s)=>s.Contains(delimiter)||s.Contains('"')||s.Contains('\r')||s.Contains('\n') ? "\""+s.Replace("\"","\"\"")+"\"" : s;
        if(header&&data.Columns.Count>0)w.WriteLine(string.Join(delimiter,data.Columns.Select(Escape)));
        foreach(var r in data.Rows)w.WriteLine(string.Join(delimiter,data.Columns.Select(c=>Escape(Scalar.Text(r.GetValueOrDefault(c))))));
    }
}
public sealed class JsonFormat : IReader,IWriter
{
    public DataSet Read(string path,Config config){using var stream=File.OpenRead(path);using var doc=JsonDocument.Parse(stream);if(doc.RootElement.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("JSON: ожидается массив объектов.");var rows=new List<Record>();foreach(var element in doc.RootElement.EnumerateArray()){if(element.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("JSON: запись должна быть объектом.");var row=new Record();foreach(var p in element.EnumerateObject()){if(!row.TryAdd(p.Name,Scalar.Json(p.Value)))throw new InvalidDataException($"JSON: повторяющееся поле {p.Name}.");}rows.Add(row);}return DataSet.FromRows(rows);}
    public void Write(string path,DataSet data,Config config)=>File.WriteAllText(path,JsonSerializer.Serialize(data.Rows,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
}
public sealed class XmlFormat : IReader,IWriter
{
    public DataSet Read(string path,Config config){using var reader=XmlReader.Create(path,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null});var doc=XDocument.Load(reader);var rows=new List<Record>();foreach(var element in doc.Root?.Elements()??Enumerable.Empty<XElement>()){var row=new Record();foreach(var field in element.Elements()){if(field.HasElements)throw new InvalidDataException("XML: вложенные поля не поддерживаются.");if(!row.TryAdd(XmlConvert.DecodeName(field.Name.LocalName),field.Value))throw new InvalidDataException("XML: повторяющееся поле.");}rows.Add(row);}return DataSet.FromRows(rows);}
    public void Write(string path,DataSet data,Config config){var root=new XElement("records",data.Rows.Select(r=>new XElement("record",data.Columns.Select(c=>new XElement(XmlConvert.EncodeLocalName(c),Scalar.Text(r.GetValueOrDefault(c)))))));new XDocument(new XDeclaration("1.0","utf-8",null),root).Save(path);}
}
