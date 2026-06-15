using System.Globalization;
using System.Text;

namespace SkinEditorNext.Services;

public static class CsvUtil
{
    public static List<string> Split(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuote = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuote && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuote = !inQuote;
                }
                continue;
            }

            if (ch == ',' && !inQuote)
            {
                result.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        result.Add(current.ToString());
        return result;
    }

    public static string Join(IReadOnlyList<string> fields)
    {
        return string.Join(",", fields.Select(Escape));
    }

    public static int IntAt(IReadOnlyList<string> fields, int index, int fallback = 0)
    {
        if (index < 0 || index >= fields.Count) return fallback;
        return int.TryParse(fields[index].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    public static void SetInt(List<string> fields, int index, int value)
    {
        while (fields.Count <= index) fields.Add(string.Empty);
        fields[index] = value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Escape(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }

        return field;
    }
}
