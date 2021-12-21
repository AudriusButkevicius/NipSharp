﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Antlr4.Runtime;

namespace NipSharp
{
    public class Matcher
    {
        private static readonly Regex ReplaceGetStat = new(@"item.getStatEx\(([^)]+)\)");
        private readonly List<Rule> _rules = new();

        public Matcher()
        {
        }

        public Matcher(string path)
        {
            AddPath(path);
        }

        public Matcher(IEnumerable<string> lines)
        {
            foreach (string line in lines)
            {
                AddRule(line);
            }
        }

        public void AddPath(string path)
        {
            foreach (string readLine in File.ReadLines(path))
            {
                AddRule(readLine);
            }
        }

        public void AddRule(string rule)
        {
            try
            {
                rule = ReplaceGetStat.Replace(rule, "[$1]");
                rule = rule.ToLower();

                var inputStream = new AntlrInputStream(rule);
                var lexer = new NipLexer(inputStream, TextWriter.Null, TextWriter.Null);
                var tokens = new CommonTokenStream(lexer);
                var parser = new NipParser(tokens, TextWriter.Null, TextWriter.Null);
                var lineExpression = parser.line();

                var valueBag = Expression.Parameter(
                    typeof(Dictionary<string, float>), "valueBag"
                );
                var matchExpression = new ExpressionBuilder(valueBag).Visit(lineExpression);

                ParameterExpression result = Expression.Parameter(typeof(Outcome), "result");
                BlockExpression block = Expression.Block(
                    new[] { result },
                    Expression.Assign(result, matchExpression),
                    result
                );
                var ruleLambda = Expression.Lambda<Func<Dictionary<string, float>, Outcome>>(block, valueBag).Compile();
                _rules.Add(
                    new Rule
                    {
                        Line = rule,
                        Matcher = ruleLambda
                    }
                );
            }
            catch (Exception e)
            {
                throw new InvalidRuleException($"Invalid rule: {rule}", e);
            }
        }

        public Result Match(IItem item, IEnumerable<IItem> otherItems = null)
        {
            otherItems ??= Array.Empty<IItem>();
            var valueBag = CreateValueBag(item);
            var otherValuesBags = otherItems.Select(CreateValueBag).ToList();

            var outcome = Outcome.Sell;
            string outcomeLine = null;

            foreach (Rule rule in _rules)
            {
                int otherCount = otherValuesBags.Count(o => rule.Matcher.Invoke(o) == Outcome.Keep);
                valueBag["currentquantity"] = otherCount;
                switch (rule.Matcher.Invoke(valueBag))
                {
                    case Outcome.Keep:
                        return new Result
                        {
                            Outcome = Outcome.Keep,
                            Line = rule.Line,
                        };
                    case Outcome.Identify:
                        if (outcome == Outcome.Sell)
                        {
                            outcome = Outcome.Identify;
                            outcomeLine = rule.Line;
                        }

                        break;
                    case Outcome.Sell:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            return new Result
            {
                Outcome = outcome,
                Line = outcomeLine
            };
        }

        public static Dictionary<string, float> CreateValueBag(IItem item)
        {
            Dictionary<string, float> valueBag = new()
            {
                { "type", item.Type },
                { "name", item.Name },
                { "class", item.Class },
                { "color", item.Color },
                { "quality", item.Quality },
                { "flag", item.Flags },
                { "level", item.Level },
            };

            for (var i = 0; i < item.Prefixes?.Count; i++)
            {
                valueBag[$"prefix{i}"] = item.Prefixes.ElementAt(i);
            }

            for (var i = 0; i < item.Suffixes?.Count; i++)
            {
                valueBag[$"suffix{i}"] = item.Suffixes.ElementAt(i);
            }

            foreach (IStat itemStat in item.Stats ?? Array.Empty<IStat>())
            {
                (int, int?)[] combinations =
                {
                    (itemStat.Id, null),
                    (itemStat.Id, itemStat.Layer),
                };

                // For each stat alias combination, put a value in the bag for that alias.
                foreach ((int, int?) key in combinations)
                {
                    if (!NipAliases.InverseStat.Contains(key)) continue;

                    foreach (string alias in NipAliases.InverseStat[key])
                    {
                        valueBag[alias] = itemStat.Value;
                    }
                }
            }

            return valueBag;
        }
    }
}
