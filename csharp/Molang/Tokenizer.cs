using System.Globalization;

namespace IdleL.Molang;

enum Tok
{
    Num, Ident,
    Plus, Minus, Star, Slash,
    LParen, RParen, Comma,
    Question, Colon, Coalesce,
    Less, LessEq, Greater, GreaterEq, EqEq, NotEq,
    And, Or, Not, Semicolon, End
}

struct Token
{
    public Tok Type;
    public double Num;
    public string Text;
}

static class Tokenizer
{
    public static List<Token> Tokenize(string src)
    {
        var tokens = new List<Token>();          // 'out' is a C# keyword, so this is 'tokens'
        int i = 0, n = src.Length;
        while (i < n)
        {
            char c = src[i];
            if (char.IsWhiteSpace(c)) { i++; }
            else if (char.IsDigit(c))
            {
                int start = i;
                while (i < n && (char.IsDigit(src[i]) || src[i] == '.')) i++;
                double value = double.Parse(src.Substring(start, i - start), CultureInfo.InvariantCulture);
                tokens.Add(new Token { Type = Tok.Num, Num = value });
            }
            else if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < n && (char.IsLetterOrDigit(src[i]) || src[i] == '_' || src[i] == '.')) i++;
                tokens.Add(new Token { Type = Tok.Ident, Text = src.Substring(start, i - start) });
            }
            else
            {
                switch (c)
                {
                    case '+': tokens.Add(new Token { Type = Tok.Plus }); i++; break;
                    case '-': tokens.Add(new Token { Type = Tok.Minus }); i++; break;
                    case '*': tokens.Add(new Token { Type = Tok.Star }); i++; break;
                    case '/': tokens.Add(new Token { Type = Tok.Slash }); i++; break;
                    case '(': tokens.Add(new Token { Type = Tok.LParen }); i++; break;
                    case ')': tokens.Add(new Token { Type = Tok.RParen }); i++; break;
                    case ',': tokens.Add(new Token { Type = Tok.Comma }); i++; break;
                    case ':': tokens.Add(new Token { Type = Tok.Colon }); i++; break;
                    case ';': tokens.Add(new Token { Type = Tok.Semicolon }); i++; break;
                    default:
                        char next = (i + 1 < n) ? src[i + 1] : '\0';
                        if      (c == '<') { if (next == '=') { tokens.Add(new Token { Type = Tok.LessEq });    i += 2; } else { tokens.Add(new Token { Type = Tok.Less });     i += 1; } }
                        else if (c == '>') { if (next == '=') { tokens.Add(new Token { Type = Tok.GreaterEq }); i += 2; } else { tokens.Add(new Token { Type = Tok.Greater });  i += 1; } }
                        else if (c == '!') { if (next == '=') { tokens.Add(new Token { Type = Tok.NotEq });     i += 2; } else { tokens.Add(new Token { Type = Tok.Not });      i += 1; } }
                        else if (c == '?') { if (next == '?') { tokens.Add(new Token { Type = Tok.Coalesce });  i += 2; } else { tokens.Add(new Token { Type = Tok.Question });  i += 1; } }
                        else if (c == '=') { if (next == '=') { tokens.Add(new Token { Type = Tok.EqEq }); i += 2; } else throw new Exception("molang: lone '=' (did you mean '==')"); }
                        else if (c == '&') { if (next == '&') { tokens.Add(new Token { Type = Tok.And });  i += 2; } else throw new Exception("molang: lone '&' (did you mean '&&')"); }
                        else if (c == '|') { if (next == '|') { tokens.Add(new Token { Type = Tok.Or });   i += 2; } else throw new Exception("molang: lone '|' (did you mean '||')"); }
                        else throw new Exception($"molang: unexpected character '{c}'");
                        break;
                }
            }
        }
        tokens.Add(new Token { Type = Tok.End });
        return tokens;
    }
}
