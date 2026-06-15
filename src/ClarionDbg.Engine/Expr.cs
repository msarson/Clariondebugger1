using System.Globalization;

namespace ClarionDbg.Engine;

/// <summary>
/// A tiny comparison evaluator for breakpoint conditions and watch expressions.
/// Supports a single comparison "LHS op RHS" (op: =, ==, &lt;&gt;, !=, &lt;, &gt;, &lt;=, &gt;=) or a bare
/// truthiness test. Operands are numeric literals, single-quoted strings, or variable
/// names resolved through a caller-supplied lookup. Numeric when both sides parse as
/// numbers, otherwise an ordinal string compare.
/// </summary>
public static class Expr
{
    static readonly string[] Ops = { "<=", ">=", "<>", "!=", "==", "=", "<", ">" };

    public static bool EvalBool(string expr, Func<string, string?> resolve)
    {
        expr = expr.Trim();
        foreach (var op in Ops)
        {
            int i = FindOp(expr, op);
            if (i <= 0) continue;
            var lhs = Operand(expr[..i].Trim(), resolve);
            var rhs = Operand(expr[(i + op.Length)..].Trim(), resolve);
            return Compare(lhs, rhs, op);
        }
        // no operator → truthiness: nonzero number or non-empty string
        var v = Operand(expr, resolve);
        return v.Num is double d ? d != 0 : !string.IsNullOrEmpty(v.Str);
    }

    static int FindOp(string s, string op)
    {
        // skip an op that sits inside a quoted string
        bool inStr = false;
        for (int i = 0; i + op.Length <= s.Length; i++)
        {
            if (s[i] == '\'') { inStr = !inStr; continue; }
            if (!inStr && s.Substring(i, op.Length) == op)
            {
                // don't match "<"/">"/"=" that are really part of a 2-char op
                if (op.Length == 1 && i + 1 < s.Length && "=<>".IndexOf(s[i + 1]) >= 0) continue;
                if (op.Length == 1 && i > 0 && "=<>!".IndexOf(s[i - 1]) >= 0) continue;
                return i;
            }
        }
        return -1;
    }

    readonly record struct Val(double? Num, string Str);

    static Val Operand(string t, Func<string, string?> resolve)
    {
        t = t.Trim();
        if (t.Length >= 2 && t[0] == '\'' && t[^1] == '\'') return new Val(null, t[1..^1]);
        if (TryNum(t, out var n)) return new Val(n, t);
        var r = resolve(t);                        // a variable name
        if (r == null) return new Val(null, "");
        return TryNum(r, out var rn) ? new Val(rn, r) : new Val(null, r);
    }

    static bool TryNum(string s, out double n)
    {
        s = s.Trim().Replace(",", "");
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h))
        { n = h; return true; }
        return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out n);
    }

    static bool Compare(Val a, Val b, string op)
    {
        if (a.Num is double x && b.Num is double y)
            return op switch
            {
                "=" or "==" => x == y,
                "<>" or "!=" => x != y,
                "<" => x < y,
                ">" => x > y,
                "<=" => x <= y,
                ">=" => x >= y,
                _ => false
            };
        int c = string.Compare(a.Str.TrimEnd(), b.Str.TrimEnd(), StringComparison.Ordinal);
        return op switch
        {
            "=" or "==" => c == 0,
            "<>" or "!=" => c != 0,
            "<" => c < 0,
            ">" => c > 0,
            "<=" => c <= 0,
            ">=" => c >= 0,
            _ => false
        };
    }
}
