namespace IdleL.Molang;

// Pratt / precedence-climbing parser. Turns a token list into an AST (Node).
// Grammar (low -> high precedence):
//   program : ternary (';' ternary)*        -- ';' sequence, value = last (no assignment in this dialect)
//   ternary : expr ('?' ternary ':' ternary)?
//   expr    : unary (binop unary)*           -- precedence climbing
//   unary   : ('-' | '!') unary | primary
//   primary : number | ident | ident '(' args ')' | '(' ternary ')'
class Parser
{
    readonly List<Token> toks;
    int pos;

    public Parser(List<Token> tokens) { toks = tokens; pos = 0; }

    Token Peek() => toks[pos];
    Token Advance() => toks[pos++];
    public bool AtEnd => Peek().Type == Tok.End;

    void Expect(Tok t)
    {
        if (Peek().Type != t) throw new Exception($"molang: expected {t}, got {Peek().Type}");
        Advance();
    }

    // higher binds tighter; -1 = not a binary operator (stops precedence climbing)
    static int Precedence(Tok t) => t switch
    {
        Tok.Coalesce => 1,                                                  // ?? (just above ternary, below ||)
        Tok.Or => 2,
        Tok.And => 3,
        Tok.EqEq or Tok.NotEq => 4,
        Tok.Less or Tok.LessEq or Tok.Greater or Tok.GreaterEq => 5,
        Tok.Plus or Tok.Minus => 6,
        Tok.Star or Tok.Slash => 7,
        _ => -1
    };

    public Node ParseProgram()
    {
        Node node = ParseTernary();
        while (Peek().Type == Tok.Semicolon)
        {
            Advance();
            if (Peek().Type == Tok.End) break;   // tolerate a trailing ';'
            node = ParseTernary();               // ';' sequence keeps the last expression
        }
        return node;
    }

    Node ParseTernary()
    {
        Node cond = ParseExpr(0);
        if (Peek().Type == Tok.Question)
        {
            Advance();
            Node thenExpr = ParseTernary();
            Expect(Tok.Colon);
            Node elseExpr = ParseTernary();
            return new Node { kind = Node.Kind.Ternary, cond = cond, left = thenExpr, right = elseExpr };
        }
        return cond;
    }

    Node ParseExpr(int minPrec)
    {
        Node left = ParseUnary();
        while (true)
        {
            Tok op = Peek().Type;
            int prec = Precedence(op);
            if (prec < minPrec || prec < 0) break;
            Advance();
            Node right = ParseExpr(prec + 1);   // left-associative
            left = new Node { kind = Node.Kind.Binary, op = op, left = left, right = right };
        }
        return left;
    }

    Node ParseUnary()
    {
        Tok t = Peek().Type;
        if (t == Tok.Minus || t == Tok.Not)
        {
            Advance();
            return new Node { kind = Node.Kind.Unary, op = t, left = ParseUnary() };
        }
        return ParsePrimary();
    }

    Node ParsePrimary()
    {
        Token tok = Peek();
        switch (tok.Type)
        {
            case Tok.Num:
                Advance();
                return new Node { kind = Node.Kind.Number, number = tok.Num };

            case Tok.LParen:
                Advance();
                Node inner = ParseTernary();
                Expect(Tok.RParen);
                return inner;

            case Tok.Ident:
                Advance();
                if (Peek().Type == Tok.LParen)   // function call
                {
                    Advance();
                    var call = new Node { kind = Node.Kind.Call, name = tok.Text };
                    if (Peek().Type != Tok.RParen)
                    {
                        call.args.Add(ParseTernary());
                        while (Peek().Type == Tok.Comma)
                        {
                            Advance();
                            call.args.Add(ParseTernary());
                        }
                    }
                    Expect(Tok.RParen);
                    return call;
                }
                return new Node { kind = Node.Kind.Lookup, name = tok.Text };   // query./variable./etc.

            default:
                throw new Exception($"molang: unexpected token {tok.Type}");
        }
    }
}
