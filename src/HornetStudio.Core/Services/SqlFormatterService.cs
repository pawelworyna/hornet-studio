using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HornetStudio.Core.Services;

public sealed class SqlFormatterService : ISqlFormatterService
{
    private static readonly HashSet<string> TopLevelClauses = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "GROUP BY", "HAVING", "ORDER BY",
        "LIMIT", "OFFSET", "INSERT INTO", "VALUES", "UPDATE", "SET",
        "DELETE FROM", "CREATE TABLE", "ALTER TABLE", "DROP TABLE",
        "TRUNCATE TABLE", "UNION ALL", "UNION", "EXCEPT", "INTERSECT",
        "BEGIN", "COMMIT", "ROLLBACK"
    };

    private static readonly HashSet<string> MidLevelClauses = new(StringComparer.OrdinalIgnoreCase)
    {
        "LEFT JOIN", "RIGHT JOIN", "INNER JOIN", "CROSS JOIN", "FULL JOIN", "JOIN",
        "LEFT OUTER JOIN", "RIGHT OUTER JOIN", "FULL OUTER JOIN",
        "AND", "OR"
    };

    private static readonly HashSet<string> CommonKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "GROUP", "BY", "HAVING", "ORDER",
        "LIMIT", "OFFSET", "INSERT", "INTO", "VALUES", "UPDATE", "SET",
        "DELETE", "CREATE", "TABLE", "ALTER", "DROP", "TRUNCATE",
        "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "CROSS", "OUTER",
        "ON", "AND", "OR", "NOT", "IN", "EXISTS", "BETWEEN", "LIKE", "ILIKE",
        "IS", "NULL", "TRUE", "FALSE", "AS", "ASC", "DESC", "DISTINCT",
        "UNION", "ALL", "CASE", "WHEN", "THEN", "ELSE", "END", "OVER",
        "PARTITION", "DEFAULT", "PRIMARY", "KEY", "FOREIGN", "REFERENCES",
        "CONSTRAINT", "INDEX", "VIEW", "DATABASE", "SCHEMA"
    };

    public string Format(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return string.Empty;

        var trimmed = sql.Trim();

        // 1. Check if it's pure JSON
        if ((trimmed.StartsWith('{') && trimmed.EndsWith('}')) ||
            (trimmed.StartsWith('[') && trimmed.EndsWith(']')))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
            }
            catch
            {
                // Fall back to SQL formatter
            }
        }

        // 2. Check if it's MongoDB command (e.g. db.users.find({...}))
        if (trimmed.StartsWith("db.", StringComparison.OrdinalIgnoreCase))
        {
            return FormatMongoQuery(trimmed);
        }

        // 3. Format standard SQL
        return FormatSqlInternal(trimmed);
    }

    private static string FormatMongoQuery(string mql)
    {
        // If there's an embedded JSON object inside (...), prettify the JSON part
        var firstParen = mql.IndexOf('(');
        var lastParen = mql.LastIndexOf(')');

        if (firstParen > 0 && lastParen > firstParen + 1)
        {
            var prefix = mql[..(firstParen + 1)];
            var inner = mql.Substring(firstParen + 1, lastParen - firstParen - 1).Trim();
            var suffix = mql[lastParen..];

            if (inner.StartsWith('{') && inner.EndsWith('}'))
            {
                try
                {
                    using var doc = JsonDocument.Parse(inner);
                    var formattedJson = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
                    // Indent the JSON by 2 spaces
                    var indentedLines = string.Join("\n  ", formattedJson.Split('\n'));
                    return $"{prefix}\n  {indentedLines}\n{suffix}";
                }
                catch
                {
                    // Ignore and return as is
                }
            }
        }

        return mql;
    }

    private static string FormatSqlInternal(string sql)
    {
        var tokens = Tokenize(sql);
        if (tokens.Count == 0) return sql;

        var sb = new StringBuilder();
        var indentLevel = 0;
        var parenDepth = 0;
        var afterSelect = false;

        const string indentStr = "  ";

        void AppendNewline(int indent)
        {
            sb.Append('\n');
            for (var i = 0; i < indent; i++)
            {
                sb.Append(indentStr);
            }
        }

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var nextToken = i + 1 < tokens.Count ? tokens[i + 1] : string.Empty;

            // Two-word clause matching (e.g. "GROUP BY", "ORDER BY", "INNER JOIN", "UNION ALL")
            var combinedTwo = $"{token} {nextToken}".ToUpperInvariant();
            if (TopLevelClauses.Contains(combinedTwo))
            {
                if (sb.Length > 0 && sb[^1] != '\n') AppendNewline(0);
                sb.Append(combinedTwo);
                indentLevel = 1;
                afterSelect = false;
                i++; // Skip next token
                continue;
            }
            if (MidLevelClauses.Contains(combinedTwo))
            {
                if (sb.Length > 0 && sb[^1] != '\n') AppendNewline(1);
                sb.Append(combinedTwo);
                i++; // Skip next token
                continue;
            }

            var upper = token.ToUpperInvariant();

            // Track parentheses
            if (token == "(")
            {
                parenDepth++;
                sb.Append(" (");
                continue;
            }
            if (token == ")")
            {
                parenDepth = Math.Max(0, parenDepth - 1);
                sb.Append(')');
                continue;
            }

            // Top-level clause keywords (only when outside subqueries / parentheses)
            if (parenDepth == 0 && TopLevelClauses.Contains(upper))
            {
                if (sb.Length > 0 && sb[^1] != '\n') AppendNewline(0);
                sb.Append(upper);
                indentLevel = 1;
                afterSelect = upper == "SELECT";
                continue;
            }

            // Mid-level clause keywords (JOIN, AND, OR)
            if (parenDepth == 0 && MidLevelClauses.Contains(upper))
            {
                if (sb.Length > 0 && sb[^1] != '\n') AppendNewline(1);
                sb.Append(upper);
                sb.Append(' ');
                continue;
            }

            // Commas at top-level inside SELECT break onto a new line
            if (token == "," && parenDepth == 0 && afterSelect)
            {
                sb.Append(',');
                AppendNewline(indentLevel);
                continue;
            }

            // Semicolons
            if (token == ";")
            {
                sb.Append(';');
                AppendNewline(0);
                indentLevel = 0;
                afterSelect = false;
                continue;
            }

            // Add space before identifier/operator if needed
            if (sb.Length > 0 && sb[^1] != '\n' && sb[^1] != ' ' && sb[^1] != '(')
            {
                sb.Append(' ');
            }

            // Uppercase recognized standard SQL keywords
            if (CommonKeywords.Contains(upper))
            {
                sb.Append(upper);
            }
            else
            {
                sb.Append(token);
            }
        }

        return sb.ToString().Trim();
    }

    private static List<string> Tokenize(string sql)
    {
        var tokens = new List<string>();
        var len = sql.Length;
        var i = 0;

        while (i < len)
        {
            var ch = sql[i];

            // Whitespace
            if (char.IsWhiteSpace(ch))
            {
                i++;
                continue;
            }

            // Single line comment --
            if (ch == '-' && i + 1 < len && sql[i + 1] == '-')
            {
                var end = sql.IndexOf('\n', i);
                if (end == -1) end = len;
                tokens.Add(sql[i..end].TrimEnd());
                i = end;
                continue;
            }

            // Multi line comment /* ... */
            if (ch == '/' && i + 1 < len && sql[i + 1] == '*')
            {
                var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end == -1) end = len;
                else end += 2;
                tokens.Add(sql[i..end]);
                i = end;
                continue;
            }

            // String literal '...'
            if (ch == '\'')
            {
                var j = i + 1;
                while (j < len)
                {
                    if (sql[j] == '\'')
                    {
                        if (j + 1 < len && sql[j + 1] == '\'')
                        {
                            j += 2; // Escaped quote
                            continue;
                        }
                        j++;
                        break;
                    }
                    j++;
                }
                tokens.Add(sql[i..j]);
                i = j;
                continue;
            }

            // Double quoted identifier "..."
            if (ch == '"')
            {
                var j = i + 1;
                while (j < len && sql[j] != '"') j++;
                if (j < len) j++;
                tokens.Add(sql[i..j]);
                i = j;
                continue;
            }

            // Backtick identifier `...`
            if (ch == '`')
            {
                var j = i + 1;
                while (j < len && sql[j] != '`') j++;
                if (j < len) j++;
                tokens.Add(sql[i..j]);
                i = j;
                continue;
            }

            // Punctuation and single char operators
            if (ch is ',' or ';' or '(' or ')' or '=')
            {
                tokens.Add(ch.ToString());
                i++;
                continue;
            }

            // Multi-char operators <=, >=, !=, <>, etc.
            if (ch is '<' or '>' or '!' or '+' or '-' or '*' or '/')
            {
                if (i + 1 < len && (sql[i + 1] == '=' || sql[i + 1] == '>'))
                {
                    tokens.Add(sql.Substring(i, 2));
                    i += 2;
                }
                else
                {
                    tokens.Add(ch.ToString());
                    i++;
                }
                continue;
            }

            // Word or identifier
            var start = i;
            while (i < len && !char.IsWhiteSpace(sql[i]) &&
                   sql[i] != ',' && sql[i] != ';' && sql[i] != '(' && sql[i] != ')' &&
                   sql[i] != '=' && sql[i] != '<' && sql[i] != '>' && sql[i] != '\'' &&
                   sql[i] != '"' && sql[i] != '`')
            {
                i++;
            }
            tokens.Add(sql[start..i]);
        }

        return tokens;
    }
}
