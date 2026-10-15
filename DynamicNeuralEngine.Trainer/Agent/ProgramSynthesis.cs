using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

public sealed record CodeExample(long[] Inputs, long Output);
public sealed record SynthesizedExpression(string Source, int Candidates, Func<long[], long> Evaluate);

// Bounded inductive synthesis, not a lookup table or a pretrained code model.
// The grammar is supplied; an expression is selected using training examples.
public static class ProgramSynthesis
{
    private sealed record Expression(string Code, Func<long[], long> Evaluate);

    public static SynthesizedExpression? Find(string[] variables, IReadOnlyList<CodeExample> examples, int maximumCandidates = 20000)
    {
        var levels = new List<Expression>[8];
        for (int i = 0; i < levels.Length; i++) levels[i] = new();
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        int candidates = 0;
        SynthesizedExpression? found = null;
        bool Add(int cost, Expression expression)
        {
            if (++candidates > maximumCandidates) return false;
            long[] values;
            try { values = examples.Select(example => expression.Evaluate(example.Inputs)).ToArray(); }
            catch (ArithmeticException) { return true; }
            string signature = string.Join(',', values);
            if (!fingerprints.Add(signature)) return true;
            levels[cost].Add(expression);
            if (values.SequenceEqual(examples.Select(example => example.Output)))
                found = new SynthesizedExpression(expression.Code, candidates, expression.Evaluate);
            return true;
        }
        for (int i = 0; i < variables.Length; i++) {
            int index = i;
            Add(1, new Expression("@" + variables[i], input => input[index]));
            if (found is not null) return found;
        }
        foreach (long constant in new long[] { 0, 1, 2, -1 }) {
            Add(1, new Expression(constant.ToString(CultureInfo.InvariantCulture) + "L", _ => constant));
            if (found is not null) return found;
        }
        for (int cost = 3; cost <= 7; cost += 2)
        {
            for (int leftCost = 1; leftCost < cost - 1; leftCost += 2)
            {
                int rightCost = cost - leftCost - 1;
                foreach (var left in levels[leftCost].ToArray()) foreach (var right in levels[rightCost].ToArray())
                {
                    foreach (char operation in new[] { '+', '-', '*', '/', '%' })
                    {
                        var expression = new Expression($"({left.Code} {operation} {right.Code})", input => {
                            long a = left.Evaluate(input), b = right.Evaluate(input);
                            return operation switch { '+' => checked(a + b), '-' => checked(a - b), '*' => checked(a * b), '/' => checked(a / b), _ => a % b };
                        });
                        if (!Add(cost, expression)) return null;
                        if (found is not null) return found;
                    }
                }
            }
        }
        return null;
    }
}
