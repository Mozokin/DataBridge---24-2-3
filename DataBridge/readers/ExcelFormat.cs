using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
namespace DataBridge;
public sealed class ExcelFormat : IReader,IWriter
{
    static readonly XNamespace S="http://schemas.openxmlformats.org/spreadsheetml/2006/main", R="http://schemas.openxmlformats.org/officeDocument/2006/relationships", P="http://schemas.openxmlformats.org/package/2006/relationships", C="http://schemas.openxmlformats.org/package/2006/content-types";
    static XDocument Load(ZipArchive zip,string name){var entry=zip.GetEntry(name)??throw new InvalidDataException($"Excel: отсутствует {name}.");using var stream=entry.Open();using var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null});return XDocument.Load(reader);}
    static string Target(string target)=>new Uri(new Uri("https://local.invalid/xl/workbook.xml"),target).AbsolutePath.TrimStart('/');
    static int Column(string? reference,int fallback){if(string.IsNullOrEmpty(reference))return fallback;int n=0;foreach(char c in reference.ToUpperInvariant()){if(c<'A'||c>'Z')break;n=checked(n*26+c-'A'+1);}if(n<1||n>16384)throw new InvalidDataException("Excel: некорректный адрес ячейки.");return n-1;}
    static string Address(int column){var result="";for(int n=column+1;n>0;n=(n-1)/26)result=(char)('A'+(n-1)%26)+result;return result;}
    public DataSet Read(string path,Config config)
    {
        using var zip=ZipFile.OpenRead(path);var book=Load(zip,"xl/workbook.xml");var rels=Load(zip,"xl/_rels/workbook.xml.rels");var first=book.Descendants(S+"sheet").FirstOrDefault()??throw new InvalidDataException("Excel: нет листов.");var id=(string?)first.Attribute(R+"id");
        var target=rels.Root!.Elements(P+"Relationship").FirstOrDefault(e=>(string?)e.Attribute("Id")==id)?.Attribute("Target")?.Value??throw new InvalidDataException("Excel: не найден первый лист.");
        var shared=new List<string>();if(zip.GetEntry("xl/sharedStrings.xml") is not null)shared=Load(zip,"xl/sharedStrings.xml").Descendants(S+"si").Select(e=>string.Concat(e.Descendants(S+"t").Select(t=>t.Value))).ToList();
        var dateStyles=new HashSet<int>();if(zip.GetEntry("xl/styles.xml") is not null){var styles=Load(zip,"xl/styles.xml");var custom=styles.Descendants(S+"numFmt").ToDictionary(x=>(int)x.Attribute("numFmtId")!,x=>(string?)x.Attribute("formatCode")??"");var xfs=styles.Root?.Element(S+"cellXfs")?.Elements(S+"xf").ToList()??new();for(int i=0;i<xfs.Count;i++){int f=(int?)xfs[i].Attribute("numFmtId")??0;var fmt=custom.GetValueOrDefault(f,"").ToLowerInvariant();if(f is >=14 and <=17 or 22 or >=27 and <=36 or >=50 and <=58 || (fmt.Contains('y')&&fmt.Contains('d')))dateStyles.Add(i);}}
        bool date1904=(string?)book.Root?.Element(S+"workbookPr")?.Attribute("date1904") is "1" or "true";
        var table=new List<Dictionary<int,object?>>();foreach(var row in Load(zip,Target(target)).Descendants(S+"sheetData").Elements(S+"row")){
            var cells=new Dictionary<int,object?>();int pos=0;foreach(var cell in row.Elements(S+"c")){int col=Column((string?)cell.Attribute("r"),pos);pos=col+1;var type=(string?)cell.Attribute("t");var v=cell.Element(S+"v")?.Value;object? value=v;
                switch(type){case "inlineStr":value=string.Concat(cell.Descendants(S+"t").Select(t=>t.Value));break;case "s":if(!int.TryParse(v,out int idx)||idx<0||idx>=shared.Count)throw new InvalidDataException("Excel: некорректный индекс общей строки.");value=shared[idx];break;case "b":value=v=="1";break;case "e":value=v;break;case "str":case "d":break;default:if(v is not null&&decimal.TryParse(v,NumberStyles.Float,CultureInfo.InvariantCulture,out decimal num)){if(dateStyles.Contains((int?)cell.Attribute("s")??0))value=DateTime.FromOADate((double)num+(date1904?1462:0)).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);else if(long.TryParse(v,NumberStyles.Integer,CultureInfo.InvariantCulture,out long l))value=l;else value=num;}break;}
                cells[col]=value;
            }if(cells.Count>0)table.Add(cells);
        }
        if(table.Count==0)return new(Array.Empty<string>(),Array.Empty<Record>());
        int width=table[0].Keys.Max()+1;var columns=Enumerable.Range(0,width).Select(i=>Scalar.Text(table[0].GetValueOrDefault(i))).ToList();if(columns.Any(string.IsNullOrWhiteSpace)||columns.Distinct().Count()!=width)throw new InvalidDataException("Excel: первая строка должна содержать уникальные непустые заголовки.");
        var rows=new List<Record>();foreach(var line in table.Skip(1)){if(line.Keys.Any(k=>k>=width&&!Scalar.Empty(line[k])))throw new InvalidDataException("Excel: данные за пределами заголовков.");var record=new Record();for(int i=0;i<width;i++)record[columns[i]]=line.GetValueOrDefault(i);rows.Add(record);}return new(columns,rows);
    }
    static void Save(ZipArchive zip,string path,XElement element){var entry=zip.CreateEntry(path);using var stream=entry.Open();new XDocument(new XDeclaration("1.0","utf-8",null),element).Save(stream);}
    public void Write(string path,DataSet data,Config config)
    {
        if(data.Columns.Count>16384||data.Rows.Count>1048575)throw new InvalidDataException("Excel: превышен лимит строк или столбцов листа.");
        using var stream=File.Create(path);using var zip=new ZipArchive(stream,ZipArchiveMode.Create);
        Save(zip,"[Content_Types].xml",new XElement(C+"Types",new XElement(C+"Default",new XAttribute("Extension","rels"),new XAttribute("ContentType","application/vnd.openxmlformats-package.relationships+xml")),new XElement(C+"Default",new XAttribute("Extension","xml"),new XAttribute("ContentType","application/xml")),new XElement(C+"Override",new XAttribute("PartName","/xl/workbook.xml"),new XAttribute("ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),new XElement(C+"Override",new XAttribute("PartName","/xl/worksheets/sheet1.xml"),new XAttribute("ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"))));
        XElement Rel(string id,string type,string target)=>new(P+"Relationship",new XAttribute("Id",id),new XAttribute("Type",R.NamespaceName+"/"+type),new XAttribute("Target",target));
        Save(zip,"_rels/.rels",new XElement(P+"Relationships",Rel("rId1","officeDocument","xl/workbook.xml")));
        Save(zip,"xl/workbook.xml",new XElement(S+"workbook",new XAttribute(XNamespace.Xmlns+"r",R),new XElement(S+"sheets",new XElement(S+"sheet",new XAttribute("name","Data"),new XAttribute("sheetId",1),new XAttribute(R+"id","rId1")))));
        Save(zip,"xl/_rels/workbook.xml.rels",new XElement(P+"Relationships",Rel("rId1","worksheet","worksheets/sheet1.xml")));
        XElement Cell(object? value,int i,int row){var cell=new XElement(S+"c",new XAttribute("r",Address(i)+row));if(value is null)return cell;if(value is bool b){cell.Add(new XAttribute("t","b"),new XElement(S+"v",b?"1":"0"));}else if(value is long or int or decimal or double or float){cell.Add(new XElement(S+"v",Scalar.Text(value)));}else{cell.Add(new XAttribute("t","inlineStr"),new XElement(S+"is",new XElement(S+"t",new XAttribute(XNamespace.Xml+"space","preserve"),Scalar.Text(value))));}return cell;}
        XElement Row(IEnumerable<object?> values,int r)=>new(S+"row",new XAttribute("r",r),values.Select((v,i)=>Cell(v,i,r)));
        var sheet=new XElement(S+"sheetData");if(data.Columns.Count>0){sheet.Add(Row(data.Columns.Cast<object?>(),1));for(int i=0;i<data.Rows.Count;i++)sheet.Add(Row(data.Columns.Select(c=>data.Rows[i].GetValueOrDefault(c)),i+2));}Save(zip,"xl/worksheets/sheet1.xml",new XElement(S+"worksheet",sheet));
    }
}
