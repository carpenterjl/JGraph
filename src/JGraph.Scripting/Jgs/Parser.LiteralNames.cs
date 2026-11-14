namespace JGraph.Scripting.Jgs;

/// <summary>
/// The words <c>true</c> and <c>false</c> in MATLAB source (open item 205, ADR 0363). They are
/// functions there, not keywords, so a workspace may bind either: <c>true = 1</c>,
/// <c>function y = f(true)</c>, <c>[a, true] = deal(1, 2)</c>. The lexer reads them as names and
/// the parser decides what a bare mention is.
/// </summary>
/// <remarks>
/// R2025b decides it from the text of a function: where a function's own code (its parameters and
/// outputs, an assignment, a <c>for</c>, <c>global</c> or <c>persistent</c>, an anonymous function's
/// parameter) binds the word, the word is that variable in its body and its nested functions;
/// where the code never binds it, the word is the built-in in that frame whatever an <c>eval</c>,
/// <c>load</c>, <c>assignin</c> or a called script puts there. So a mention in a function that
/// does not bind it reads no variable, and a mention in one that does reads the binding when
/// there is one. Outside every function (a script, the command line, eval'd text) nothing is known
/// until the code runs, and every mention may read a variable. A local or nested function named
/// after the word answers every bare mention in its file, and a <c>true.m</c> on the path every
/// mention anywhere, as a file takes any built-in's name (ADR 0149).
/// </remarks>
internal sealed partial class Parser
{
    /// <summary>One MATLAB function body being read: what it binds and what waits on that.</summary>
    private sealed class LiteralScope(LiteralScope? outer, int binds)
    {
        private readonly List<BoolLiteral> _pending = [];
        private readonly List<FnStmt> _functions = [];

        /// <summary>Which words the body binds so far (1 true, 2 false), its enclosing bodies' included.</summary>
        public int Binds { get; set; } = binds | (outer?.Binds ?? 0);

        /// <summary>A literal read in the body before anything bound its word; settled when the body ends.</summary>
        public void Defer(BoolLiteral literal) => _pending.Add(literal);

        /// <summary>
        /// Ends the body with its function: a literal whose word the body bound reads the binding,
        /// and the rest wait on the enclosing body, whose variables a nested function shares; the
        /// function and the nested functions inside it learn what the enclosing body binds as well.
        /// </summary>
        public FnStmt Adopt(FnStmt function)
        {
            _functions.Add(function);
            foreach (FnStmt inner in _functions)
            {
                inner.LiteralBindings |= Binds;
            }

            foreach (BoolLiteral literal in _pending)
            {
                if ((Binds & LiteralBit(literal.Value)) != 0)
                {
                    literal.Shadow |= LiteralShadow.Variable;
                }
                else
                {
                    outer?._pending.Add(literal);
                }
            }

            outer?._functions.AddRange(_functions);
            return function;
        }
    }

    /// <summary>The bit a word has in a binding mask: 1 for <c>true</c>, 2 for <c>false</c>.</summary>
    internal static int LiteralBit(bool value) => value ? 1 : 2;

    /// <summary>The bit of <paramref name="name"/> if it is one of the two words, else 0.</summary>
    internal static int LiteralBits(string? name) => name switch
    {
        "true" => 1,
        "false" => 2,
        _ => 0,
    };

    private static int LiteralBits(IEnumerable<string> names)
    {
        int bits = 0;
        foreach (string name in names)
        {
            bits |= LiteralBits(name);
        }

        return bits;
    }

    /// <summary>Records that the function body being read binds <paramref name="name"/>.</summary>
    private void Binds(string name)
    {
        if (_matlab && _literalScope is not null)
        {
            _literalScope.Binds |= LiteralBits(name);
        }
    }

    /// <summary>
    /// A bare <c>true</c> or <c>false</c> in MATLAB source: the literal unless a file on the path
    /// takes the word, or a variable outside every function and in a function that binds the word.
    /// </summary>
    private BoolLiteral SpelledLiteral(Token word)
    {
        var literal = new BoolLiteral(word.Text == "true") { Line = word.Line, Column = word.Column, Shadow = LiteralShadow.File };
        (_spelledLiterals ??= []).Add(literal);
        if (_literalScope is null || (_literalScope.Binds & LiteralBits(word.Text)) != 0)
        {
            literal.Shadow |= LiteralShadow.Variable;
        }
        else
        {
            _literalScope.Defer(literal);
        }

        return literal;
    }

    /// <summary>
    /// An assignment's target as written: a bare word that read as a literal is the variable it
    /// binds (<c>true = 1</c>, <c>[a, true] = deal(1, 2)</c>), and a target rooted at one of the
    /// two words (<c>true(2) = 5</c>, <c>true.x = 1</c>) binds it as well.
    /// </summary>
    private Expr AsTarget(Expr target)
    {
        if (!_matlab)
        {
            return target;
        }

        if (target is BoolLiteral literal)
        {
            string word = literal.Value ? "true" : "false";
            Binds(word);
            return new VariableExpr(word) { Line = literal.Line, Column = literal.Column };
        }

        Expr root = target;
        while (true)
        {
            switch (root)
            {
                case CallExpr call:
                    root = call.Callee;
                    continue;
                case IndexExpr index:
                    root = index.Target;
                    continue;
                case BraceIndexExpr brace:
                    root = brace.Target;
                    continue;
                case MemberExpr member:
                    root = member.Target;
                    continue;
                case VariableExpr variable:
                    Binds(variable.Name);
                    break;
            }

            return target;
        }
    }
}
