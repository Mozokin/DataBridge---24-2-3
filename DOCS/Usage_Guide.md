# Сценарии использования

Рабочая директория — корень проекта. Все команды запускаются в PowerShell или терминале.

## 1. Основной сценарий: CSV → JSON

```powershell
dotnet run --project DataBridge -- convert samples/input.csv --to json --config configs/schema.json --output samples/output.json
```

Ожидаются 20 записей: 10 принятых, 10 отклонённых; рядом с output.json появятся errors.csv и report.html. В выходном JSON name → full_name, birth_date → date_of_birth, имена приведены к верхнему регистру, email — к нижнему, удалены внешние пробелы. Сравнить с expected_output.json.

## 2. Проверка каждого формата

```powershell
dotnet run --project DataBridge -- validate samples/input.json --config configs/schema.json --report results/json/report.html
dotnet run --project DataBridge -- validate samples/input.xml --config configs/schema.json --report results/xml/report.html
dotnet run --project DataBridge -- validate samples/input.xlsx --config configs/schema.json --report results/xlsx/report.html
```

Ожидается по 10 принятых и 10 отклонённых записей. HTML открывается браузером.

## 3. Другие преобразования

```powershell
dotnet run --project DataBridge -- convert samples/input.xlsx --to xml --config configs/schema.json --output results/xlsx-to-xml/output.xml
dotnet run --project DataBridge -- convert samples/input.json --to xlsx --config configs/schema.json --output results/json-to-xlsx/output.xlsx
dotnet run --project DataBridge -- convert samples/input.xml --to csv --config configs/schema.json --output results/xml-to-csv/output.csv
dotnet run --project DataBridge -- convert samples/no_header.csv --to json --config configs/no_header.json --output results/no-header/output.json
dotnet run --project DataBridge -- convert samples/timestamp.csv --to json --config configs/timestamp.json --output results/timestamp/output.json
```

## 4. Статистика и повторная конвертация

```powershell
dotnet run --project DataBridge -- stats samples/input.xlsx
dotnet run --project DataBridge -- convert samples/output.json --to xlsx --config configs/passthrough.json --output results/roundtrip/output.xlsx
```

Для уже переименованных полей используйте подходящий новый конфиг или passthrough. `stats` выводит количество записей, столбцов, заполненных/пустых значений.
