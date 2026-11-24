using System.Globalization;
using System.Text.RegularExpressions;
namespace DataBridge;
public sealed record ValidationError(int Row,string Field,string Message,string Value);
public sealed record RejectedRecord(int Row,Record Record,List<ValidationError> Errors);
public sealed class ValidationResult
{
    public int Total {get;init;} public DataSet Valid {get;init;}=new(Array.Empty<string>(),Array.Empty<Record>()); public List<RejectedRecord> Rejected {get;init;}=new();
    public IEnumerable<ValidationError> Errors=>Rejected.SelectMany(r=>r.Errors);
}
public interface IValidator { ValidationResult Validate(DataSet data,Config config); }
public sealed class Validator : IValidator
{
    public ValidationResult Validate(DataSet data,Config config) => Validate(data,config,false);
    public ValidationResult Validate(DataSet data,Config config,bool checkTransforms)
    {
        var accepted=new List<Record>();var rejected=new List<RejectedRecord>();var seen=config.Fields.Where(f=>f.Unique).ToDictionary(f=>f.Name,f=>new HashSet<object>());
        for(int i=0;i<data.Rows.Count;i++){
            var raw=data.Rows[i];var normalized=new Record(raw);var errors=new List<ValidationError>();var unique=new List<(string Field,object Value)>();
            foreach(var rule in config.Fields){raw.TryGetValue(rule.Name,out var input);string text=Scalar.Text(input);void Error(string msg)=>errors.Add(new(i+1,rule.Name,msg,text));
                if(Scalar.Empty(input)){if(rule.Required)Error("обязательное поле не заполнено");continue;}
                if(!rule.TryValue(input,out var value)){Error($"ожидается тип {rule.Type}");continue;}
                if(rule.Min.HasValue&&rule.Compare(value!,rule.Min.Value)<0)Error("значение ниже минимума");
                if(rule.Max.HasValue&&rule.Compare(value!,rule.Max.Value)>0)Error("значение выше максимума");
                if(rule.MinLength.HasValue&&text.Length<rule.MinLength.Value)Error("строка короче допустимой длины");
                if(rule.MaxLength.HasValue&&text.Length>rule.MaxLength.Value)Error("строка длиннее допустимой длины");
                if(rule.Pattern is not null&&!Regex.IsMatch(text,rule.Pattern,RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1)))Error("значение не соответствует формату (regex)");
                if(rule.Unique){if(seen[rule.Name].Contains(value!))Error("повторяющееся значение (unique)");else unique.Add((rule.Name,value!));}
                normalized[rule.Name]=value is DateTime d ? d.ToString(rule.DateFormat,CultureInfo.InvariantCulture) : value;
            }
            if(checkTransforms&&errors.Count==0){var probe=new Record(normalized);foreach(var t in config.Transformations){if(!probe.TryGetValue(t.Field,out var v)||Scalar.Empty(v))continue;try{probe[t.Field]=Converter.Apply(t,v);}catch(InvalidDataException e){errors.Add(new(i+1,t.Field,e.Message,Scalar.Text(v)));break;}}}
            if(errors.Count==0){foreach(var item in unique)seen[item.Field].Add(item.Value);accepted.Add(normalized);}else rejected.Add(new(i+1,raw,errors));
        }
        return new(){Total=data.Rows.Count,Valid=new(data.Columns,accepted),Rejected=rejected};
    }
}
