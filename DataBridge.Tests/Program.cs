using DataBridge;
using System.Text.Json;
using System.Xml.Linq;
int passed=0,failed=0;string temp=Path.Combine(Path.GetTempPath(),"DataBridgeTests-"+Guid.NewGuid());Directory.CreateDirectory(temp);
void Test(string name,Action action){try{action();Console.WriteLine("PASS "+name);passed++;}catch(Exception ex){Console.WriteLine("FAIL "+name+": "+ex.Message);failed++;}}
void Assert(bool condition){if(!condition)throw new Exception("Assertion failed");}
void Throws<T>(Action a) where T:Exception{try{a();}catch(Exception ex) when(ex is T){return;}throw new Exception("Expected "+typeof(T).Name);}
JsonElement J(string s)=>JsonSerializer.Deserialize<JsonElement>(s);
Record R(object? value)=>new(){["v"]=value};
ValidationResult V(FieldRule rule,params object?[] values){rule.Name="v";return new Validator().Validate(DataSet.FromRows(values.Select(R)),new Config{Fields=new(){rule}});}
Test("V01 required missing",()=>Assert(V(new(){Required=true},(object?)null,"").Rejected.Count==2));
Test("V02 optional empty",()=>Assert(V(new(),(object?)null,"").Valid.Rows.Count==2));
Test("V03 integer",()=>Assert(V(new(){Type="integer"},"123","12.3","abc").Valid.Rows.Count==1));
Test("V04 decimal invariant",()=>Assert(V(new(){Type="float"},"1.25","1,25").Valid.Rows.Count==1));
Test("V05 min inclusive",()=>Assert(V(new(){Type="integer",Min=J("16")},"15","16").Rejected.Count==1));
Test("V06 max inclusive",()=>Assert(V(new(){Type="integer",Max=J("100")},"100","101").Rejected.Count==1));
Test("V07 regex",()=>Assert(V(new(){Pattern=@"^[^@]+@[^@]+\.[^@]+$"},"a@b.ru","a.b.ru").Rejected.Count==1));
Test("V08 unique normalized",()=>Assert(V(new(){Type="integer",Unique=true},"01","1","2").Rejected.Count==1));
Test("V09 string length",()=>Assert(V(new(){MinLength=2,MaxLength=4},"a","ab","abcd","abcde").Rejected.Count==2));
Test("V10 dates leap year",()=>Assert(V(new(){Type="date"},"2024-02-29","2023-02-29").Rejected.Count==1));
Test("V11 date range",()=>Assert(V(new(){Type="date",Min=J("\"2000-01-01\""),Max=J("\"2020-12-31\"")},"1999-12-31","2000-01-01","2020-12-31","2021-01-01").Rejected.Count==2));
Test("V12 boolean",()=>Assert(V(new(){Type="boolean"},"true","0","maybe").Valid.Rows.Count==2));
Test("V13 rejected row does not reserve ID",()=>{var c=new Config{Fields=new(){new(){Name="id",Type="integer",Unique=true},new(){Name="name",Required=true}}};var d=DataSet.FromRows(new[]{new Record{["id"]="1",["name"]=""},new Record{["id"]="1",["name"]="OK"}});Assert(new Validator().Validate(d,c).Valid.Rows.Count==1);});
Test("V14 string rejects numeric JSON",()=>Assert(V(new(),123L,"123").Rejected.Count==1));
Test("C01 CSV quotes and newline",()=>{var r=CsvFormat.Parse(new StringReader("a,b\r\n\"a,b\",\"line\n\"\"quoted\"\"\"\r\n"),',');Assert(r.Count==2&&r[1][0]=="a,b"&&r[1][1]=="line\n\"quoted\"");});
Test("C02 CSV delimiter no header",()=>{var p=Path.Combine(temp,"no.csv");File.WriteAllText(p,"1;Alice\n2;Bob");var d=Formats.Read(p,new Config{Csv=new(){Delimiter=";",HasHeader=false,Columns=new(){"id","name"}}});Assert(d.Rows.Count==2&&Scalar.Text(d.Rows[1]["name"])=="Bob");});
Test("C03 invalid CSV",()=>Throws<InvalidDataException>(()=>CsvFormat.Parse(new StringReader("a,\"b"),',')));
Test("C04 malformed JSON",()=>{var p=Path.Combine(temp,"bad.json");File.WriteAllText(p,"[");Throws<JsonException>(()=>Formats.Read(p,new()));});
Test("C05 mapping trim upper lower replace",()=>{var c=new Config{Mapping=new(){["v"]="name"},Transformations=new(){new(){Field="v",Operation="trim"},new(){Field="v",Operation="upper"},new(){Field="v",Operation="lower"},new(){Field="v",Operation="replace",Values=new(){["alice"]="Alice"}}}};var d=new Converter().Convert(DataSet.FromRows(new[]{R(" Alice ")}),c);Assert(Scalar.Text(d.Rows[0]["name"])=="Alice"&&!d.Rows[0].ContainsKey("v"));});
Test("C06 timestamp",()=>Assert((string)Converter.Apply(new(){Operation="timestamp_to_date"},"0")=="1970-01-01"));
Test("C07 mapping collision",()=>Throws<InvalidDataException>(()=>new Converter().Convert(DataSet.FromRows(new[]{new Record{["a"]=1,["b"]=2}}),new(){Mapping=new(){["a"]="b"}})));
foreach(var ext in new[]{"csv","json","xml","xlsx"})Test("C08 round trip "+ext,()=>{var p=Path.Combine(temp,"round."+ext);var d=DataSet.FromRows(new[]{new Record{["id"]=1L,["name"]="Иван & <тест>",["email"]="a@b.ru"}});Formats.Write(p,ext,d,new());var x=Formats.Read(p,new());Assert(x.Rows.Count==1&&Scalar.Text(x.Rows[0]["id"])=="1"&&Scalar.Text(x.Rows[0]["name"])=="Иван & <тест>");});
Test("C09 CSV to JSON",()=>{var p=Path.Combine(temp,"in.csv");File.WriteAllText(p,"id,name\n1,Иван\n");var d=Formats.Read(p,new());var v=new Validator().Validate(d,new(){Fields=new(){new(){Name="id",Type="integer"}}});var o=Path.Combine(temp,"out.json");Formats.Write(o,"json",v.Valid,new());using var doc=JsonDocument.Parse(File.ReadAllText(o));Assert(doc.RootElement[0].GetProperty("id").GetInt64()==1);});
Test("C10 bad timestamp rejected",()=>{var c=new Config{Transformations=new(){new(){Field="v",Operation="timestamp_to_date"}}};var d=DataSet.FromRows(new[]{R("bad"),R("0")});Assert(new Validator().Validate(d,c,true).Rejected.Count==1);});
Test("R01 report escapes HTML and preserves rejected rows",()=>{var d=DataSet.FromRows(new[]{R("<script>")});var v=new Validator().Validate(d,new(){Fields=new(){new(){Name="v",Pattern="^ok$"}}});string h=Path.Combine(temp,"report.html"),e=Path.Combine(temp,"errors.csv");new Reporter().Write(h,e,d,v);Assert(File.ReadAllText(h).Contains("&lt;script&gt;")&&File.ReadAllText(e).Contains("<script>"));});
Test("CLI01 invalid file returns 1",()=>Assert(CommandLine.Run(new[]{"stats",Path.Combine(temp,"missing.csv")})==1));
try{Directory.Delete(temp,true);}catch(IOException){}
Console.WriteLine($"TOTAL {passed+failed}; PASSED {passed}; FAILED {failed}");return failed==0?0:1;
