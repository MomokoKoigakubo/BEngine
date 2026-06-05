namespace IdleL.Molang;

// AST node. shared_ptr<const Node> in C++ becomes a plain reference here (GC owns it).
class Node
{
    public enum Kind { Number, Binary, Unary, Ternary, Lookup, Call }
    public Kind kind;

    public double number;                 // Number
    public Tok op;                        // Binary / Unary operator
    public string name = "";              // Lookup / Call identifier (e.g. "math.sin")
    public Node? cond, left, right;       // Binary: left/right; Unary: left; Ternary: cond ? left : right
    public List<Node> args = new();       // Call arguments
}
