using System.Net;
using System.Text;
namespace DataBridge;
public interface IReporter { void Write(string reportPath,string errorsPath,DataSet source,ValidationResult result); }
public sealed class Reporter : IReporter
{
    public void Write(string reportPath,string errorsPath,DataSet source,ValidationResult result)
    {
        string ErrorColumn(string name){while(source.Columns.Contains(name))name="_"+name;return name;}string rowKey=ErrorColumn("_row"),errorKey=ErrorColumn("_errors");
        var rows=result.Rejected.Select(r=>{var row=new Record(r.Record){[rowKey]=r.Row,[errorKey]=string.Join("; ",r.Errors.Select(e=>$"{e.Field}: {e.Message}"))};return row;});CsvFormat.WriteRows(errorsPath,new DataSet(new[]{rowKey}.Concat(source.Columns).Append(errorKey),rows));
        string H(string s)=>WebUtility.HtmlEncode(s);var body=new StringBuilder();foreach(var e in result.Errors)body.Append($"<tr><td>{e.Row}</td><td>{H(e.Field)}</td><td>{H(e.Value)}</td><td>{H(e.Message)}</td></tr>");if(body.Length==0)body.Append("<tr><td colspan='4'>Ошибок нет</td></tr>");
        File.WriteAllText(reportPath,$$"""
<!doctype html><html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>DataBridge — отчёт</title><style>body{font-family:system-ui,sans-serif;background:#f5f7fa;color:#17263b;margin:40px auto;max-width:1050px;padding:0 18px}header{border-bottom:3px solid #137c86}.metrics{display:flex;gap:12px;flex-wrap:wrap;margin:24px 0}.metric{background:white;border:1px solid #d6e0e8;border-radius:8px;padding:16px;min-width:150px}strong{display:block;font-size:30px}table{width:100%;border-collapse:collapse;background:white}th,td{border:1px solid #d6e0e8;padding:10px;text-align:left;overflow-wrap:anywhere}th{background:#e1eef0}small{color:#596979}</style></head><body><header><h1>DataBridge / Отчёт валидации</h1><p>Проверка качества данных</p></header><div class="metrics"><div class="metric">Всего записей<strong>{{result.Total}}</strong></div><div class="metric">Успешно<strong>{{result.Valid.Rows.Count}}</strong></div><div class="metric">Некорректных<strong>{{result.Rejected.Count}}</strong></div><div class="metric">Нарушений правил<strong>{{result.Errors.Count()}}</strong></div></div><h2>Таблица ошибок</h2><table><thead><tr><th>Запись</th><th>Поле</th><th>Значение</th><th>Причина</th></tr></thead><tbody>{{body}}</tbody></table><p><small>Номера записей отсчитываются от 1 без заголовка. Одна запись может содержать несколько ошибок.</small></p></body></html>
""");
    }
}
