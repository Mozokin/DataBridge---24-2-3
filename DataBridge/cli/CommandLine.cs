using System.Text;
using System.Text.Json;
using System.Xml;
namespace DataBridge;
public static class CommandLine
{
    public static int Run(string[] args)
    {
        Console.OutputEncoding=Encoding.UTF8;
        try{
            if(args.Length==0||args[0] is "--help" or "-h" or "help"){Help();return 0;}
            string command=args[0].ToLowerInvariant();if(command is not ("convert" or "validate" or "stats"))throw new ArgumentException("Неизвестная команда. Используйте --help.");if(args.Length<2||args[1].StartsWith("--"))throw new ArgumentException("Укажите входной файл.");
            string input=args[1];var options=new Dictionary<string,string>();for(int i=2;i<args.Length;i+=2){if(i+1>=args.Length||!args[i].StartsWith("--")||args[i+1].StartsWith("--"))throw new ArgumentException($"Для параметра {args[i]} нужно значение.");if(!options.TryAdd(args[i],args[i+1]))throw new ArgumentException($"Повтор параметра {args[i]}.");}
            string[] allowed=command switch{"convert"=>new[]{"--config","--to","--output","--report","--errors"},"validate"=>new[]{"--config","--report","--errors"},_=>new[]{"--config"}};foreach(var k in options.Keys)if(!allowed.Contains(k))throw new ArgumentException($"Неизвестный параметр {k}.");
            string? configPath=options.GetValueOrDefault("--config");if(command!="stats"&&configPath is null)throw new ArgumentException("Для проверки укажите --config schema.json.");var config=Config.Load(configPath);var source=Formats.Read(input,config);
            if(command=="stats"){Console.WriteLine($"Формат: {Formats.Format(input)}\nВсего записей: {source.Rows.Count}\nСтолбцов: {source.Columns.Count}");foreach(var c in source.Columns)Console.WriteLine($" - {c}: заполнено {source.Rows.Count(r=>!Scalar.Empty(r.GetValueOrDefault(c)))}, пустых {source.Rows.Count(r=>Scalar.Empty(r.GetValueOrDefault(c)))}");return 0;}
            var result=new Validator().Validate(source,config,command=="convert");string? output=null;string? format=null;
            if(command=="convert"){format=Formats.Normalize(options.GetValueOrDefault("--to")??throw new ArgumentException("Укажите --to csv/json/xml/xlsx."));if(!Formats.Supported(format))throw new ArgumentException("Неподдерживаемый выходной формат.");output=options.GetValueOrDefault("--output",$"output.{format}");if(Formats.Format(output)!=format)throw new ArgumentException("Расширение выходного файла должно совпадать с --to.");}
            string folder=Path.GetDirectoryName(Path.GetFullPath(output??options.GetValueOrDefault("--report","report.html")))!;string report=options.GetValueOrDefault("--report",Path.Combine(folder,"report.html")),errors=options.GetValueOrDefault("--errors",Path.Combine(folder,"errors.csv"));
            var files=new[]{input,configPath,output,report,errors}.Where(p=>p is not null).Select(p=>Path.GetFullPath(p!)).ToList();if(files.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=files.Count)throw new ArgumentException("Вход, конфиг, выход, отчёт и ошибки должны иметь разные пути.");
            // Conversion is prepared before any files are written.
            DataSet? converted=output is null?null:new Converter().Convert(result.Valid,config);
            foreach(var path in new[]{output,report,errors}.Where(p=>p is not null))Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path!))!);
            if(output is not null)Formats.Write(output,format!,converted!,config);new Reporter().Write(report,errors,source,result);
            Console.WriteLine($"Всего записей: {result.Total}\nУспешно обработано: {result.Valid.Rows.Count}\nОшибок (записей): {result.Rejected.Count}");foreach(var e in result.Errors)Console.WriteLine($" - Запись {e.Row}: поле \"{e.Field}\" — {e.Message} ({e.Value})");if(output is not null)Console.WriteLine($"Результат сохранён: {output}");Console.WriteLine($"Ошибки сохранены: {errors}\nОтчёт сохранён: {report}");return result.Rejected.Count==0?0:2;
        }catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or XmlException or ArgumentException or InvalidOperationException or FormatException or OverflowException or System.Text.RegularExpressions.RegexMatchTimeoutException){Console.Error.WriteLine("Ошибка: "+e.Message);return 1;}
    }
    static void Help()=>Console.WriteLine("""
DataBridge — конвертер и валидатор данных
  databridge convert input.csv --to json --config schema.json --output output.json
  databridge validate input.json --config schema.json --report report.html
  databridge stats input.xlsx [--config schema.json]
Форматы: csv, json, xml, xlsx. convert/validate: --report путь --errors путь.
Коды завершения: 0 — успешно; 1 — ошибка запуска/файла; 2 — найдены некорректные записи, отчёты сохранены.
""");
}
