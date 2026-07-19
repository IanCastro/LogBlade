using System;
using System.Collections.Generic;
using System.Text;

internal sealed class DisplayParserOutputTemplate
{
    private readonly Token[] _tokens;

    private DisplayParserOutputTemplate(Token[] tokens)
    {
        _tokens = tokens;
    }

    public static DisplayParserOutputTemplate Compile(string template)
    {
        ArgumentNullException.ThrowIfNull(template);

        List<Token> tokens = new();
        StringBuilder literal = new();
        for (int i = 0; i < template.Length; i++)
        {
            char current = template[i];
            if (current != '$' || i + 1 >= template.Length)
            {
                literal.Append(current);
                continue;
            }

            char next = template[i + 1];
            if (next == '$')
            {
                literal.Append('$');
                i++;
                continue;
            }

            if (next is >= '0' and <= '9')
            {
                FlushLiteral(tokens, literal);
                int selectorStart = i + 1;
                int selectorEnd = selectorStart + 1;
                while (selectorEnd < template.Length && template[selectorEnd] is >= '0' and <= '9')
                {
                    selectorEnd++;
                }

                tokens.Add(CreatePlaceholder(template.Substring(selectorStart, selectorEnd - selectorStart)));
                i = selectorEnd - 1;
                continue;
            }

            if (next != '{')
            {
                literal.Append(current);
                continue;
            }

            int end = template.IndexOf('}', i + 2);
            if (end < 0)
            {
                throw new ArgumentException("Output template contains an unterminated placeholder.", nameof(template));
            }

            string expression = template.Substring(i + 2, end - i - 2).Trim();
            if (expression.Length == 0)
            {
                throw new ArgumentException("Output template contains an empty placeholder.", nameof(template));
            }

            FlushLiteral(tokens, literal);
            tokens.Add(CreatePlaceholder(expression));
            i = end;
        }

        FlushLiteral(tokens, literal);
        return new DisplayParserOutputTemplate(tokens.ToArray());
    }

    public string Render(Func<string, string?> resolveValue)
    {
        ArgumentNullException.ThrowIfNull(resolveValue);

        if (_tokens.Length == 0)
        {
            return string.Empty;
        }

        if (_tokens.Length == 1 && _tokens[0].Selector is null)
        {
            return _tokens[0].Value;
        }

        StringBuilder output = new();
        for (int i = 0; i < _tokens.Length; i++)
        {
            Token token = _tokens[i];
            if (token.Selector is null)
            {
                output.Append(token.Value);
                continue;
            }

            string? resolved = resolveValue(token.Selector);
            if (resolved is null)
            {
                continue;
            }

            output.Append(token.Transform switch
            {
                OutputTransform.Upper => resolved.ToUpperInvariant(),
                OutputTransform.Lower => resolved.ToLowerInvariant(),
                _ => resolved
            });
        }

        return output.ToString();
    }

    private static Token CreatePlaceholder(string expression)
    {
        OutputTransform transform = OutputTransform.None;
        string selector = expression;
        int separator = expression.IndexOf(':');
        if (separator > 0)
        {
            string candidate = expression.Substring(0, separator).Trim();
            if (string.Equals(candidate, "upper", StringComparison.OrdinalIgnoreCase))
            {
                transform = OutputTransform.Upper;
                selector = expression.Substring(separator + 1).Trim();
            }
            else if (string.Equals(candidate, "lower", StringComparison.OrdinalIgnoreCase))
            {
                transform = OutputTransform.Lower;
                selector = expression.Substring(separator + 1).Trim();
            }
        }

        if (selector.Length == 0)
        {
            throw new ArgumentException("Output template contains a placeholder without a selector.", nameof(expression));
        }

        return new Token(string.Empty, selector, transform);
    }

    private static void FlushLiteral(List<Token> tokens, StringBuilder literal)
    {
        if (literal.Length == 0)
        {
            return;
        }

        tokens.Add(new Token(literal.ToString(), null, OutputTransform.None));
        literal.Clear();
    }

    private readonly record struct Token(
        string Value,
        string? Selector,
        OutputTransform Transform);

    private enum OutputTransform
    {
        None,
        Upper,
        Lower
    }
}
