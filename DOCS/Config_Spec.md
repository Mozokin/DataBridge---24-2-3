# Спецификация JSON-конфигурации

Корень — JSON-объект. Все разделы необязательны; для validate/convert файл конфигурации обязателен. Пример: `configs/schema.json`.

```json
{
  "fields": [
    {"name":"id","type":"integer","required":true,"unique":true,"min":1},
    {"name":"email","type":"string","required":true,"pattern":"^[^@]+@[^@]+\\.[^@]+$"},
    {"name":"age","type":"integer","min":16,"max":100},
    {"name":"birth_date","type":"date","format":"YYYY-MM-DD","min":"1900-01-01"}
  ],
  "csv":{"delimiter":",","has_header":true},
  "mapping":{"birth_date":"date_of_birth"},
  "transformations":[{"field":"email","operation":"lower"}]
}
```

| Ключ поля | Тип | Назначение |
|---|---|---|
| name | string | Уникальное имя входного поля (регистр учитывается) |
| type | string | integer (Int64), float (Decimal), string, date, boolean |
| required | bool | Не допускает null, отсутствующее или пустое/пробельное значение |
| unique | bool | Запрет повторов среди ранее принятых записей, после нормализации |
| min, max | number или строка даты | Включительные границы чисел и дат |
| pattern | string | Регулярное выражение .NET; экранирование JSON обязательно |
| min_length, max_length | integer | Границы длины |
| format | string | Формат даты; YYYY-MM-DD или yyyy-MM-dd, по умолчанию yyyy-MM-dd |

Для чисел используется инвариантная культура (десятичная точка). Логические значения: true/false, 1/0. Числовые и логические текстовые значения CSV/XML преобразуются в соответствующие типы; JSON-число не считается строкой. Необязательные пустые поля пропускаются.

`csv.delimiter` — один символ; `csv.has_header` — наличие заголовка на входе и выходе CSV; `csv.columns` — имена для CSV без заголовка. При отсутствии columns используются имена fields, а при их отсутствии — column1, column2 и т. д.

`mapping` — словарь «старое имя → новое имя». `transformations` — упорядоченный список операций на исходных именах. Операции: trim, upper, lower, replace (values — словарь замен), timestamp_to_date (Unix timestamp в секундах, UTC). Сначала валидация, затем трансформации, затем переименование. Если нужно принимать пробелы в email, настройте regex соответственно: trim не выполняется до проверки.
