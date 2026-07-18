using System.Linq;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Structural equality over JGS syntax trees, ignoring source positions (line/column/source id). This
/// is the compatibility test for live edits while paused: two statements are equal when re-executing
/// one is indistinguishable from re-executing the other — so whitespace, comments, and edits below the
/// execution point never count as changes to the code that already ran.
/// </summary>
internal static class AstEquals
{
    /// <summary>Whether two statements are structurally identical (including their nested blocks).</summary>
    public static bool StatementsEqual(Stmt a, Stmt b)
    {
        if (!HeaderEqual(a, b))
        {
            return false;
        }

        for (int slot = 0; slot < AstChildren.SlotCount(a); slot++)
        {
            if (!BlocksEqual(AstChildren.Slot(a, slot), AstChildren.Slot(b, slot)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether two statements are structurally identical apart from the block in <paramref name="skipSlot"/>,
    /// which must exist in both. This pins a statement the interpreter is currently inside: its header and
    /// every other branch may not change, while the block being executed is compared level-by-level elsewhere.
    /// </summary>
    public static bool EqualExceptSlot(Stmt a, Stmt b, int skipSlot)
    {
        if (!HeaderEqual(a, b) || AstChildren.Slot(b, skipSlot) is null)
        {
            return false;
        }

        for (int slot = 0; slot < AstChildren.SlotCount(a); slot++)
        {
            if (slot != skipSlot && !BlocksEqual(AstChildren.Slot(a, slot), AstChildren.Slot(b, slot)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether two blocks (either possibly absent) hold pairwise equal statements.</summary>
    public static bool BlocksEqual(IReadOnlyList<Stmt>? a, IReadOnlyList<Stmt>? b)
    {
        if (a is null || b is null)
        {
            return ReferenceEquals(a, b);
        }

        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (!StatementsEqual(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether two expressions are structurally identical.</summary>
    public static bool ExpressionsEqual(Expr? a, Expr? b)
    {
        if (a is null || b is null)
        {
            return ReferenceEquals(a, b);
        }

        return (a, b) switch
        {
            (NumberLiteral x, NumberLiteral y) => x.Value.Equals(y.Value),
            (StringLiteral x, StringLiteral y) => string.Equals(x.Value, y.Value, StringComparison.Ordinal),
            (BoolLiteral x, BoolLiteral y) => x.Value == y.Value,
            (ArrayLiteral x, ArrayLiteral y) =>
                x.Elements.Count == y.Elements.Count
                && !x.Elements.Where((e, i) => !ExpressionsEqual(e, y.Elements[i])).Any(),
            (VariableExpr x, VariableExpr y) => string.Equals(x.Name, y.Name, StringComparison.Ordinal),
            (UnaryExpr x, UnaryExpr y) => x.Op == y.Op && ExpressionsEqual(x.Operand, y.Operand),
            (BinaryExpr x, BinaryExpr y) =>
                x.Op == y.Op && ExpressionsEqual(x.Left, y.Left) && ExpressionsEqual(x.Right, y.Right),
            (LogicalExpr x, LogicalExpr y) =>
                x.Op == y.Op && ExpressionsEqual(x.Left, y.Left) && ExpressionsEqual(x.Right, y.Right),
            (CallExpr x, CallExpr y) =>
                ExpressionsEqual(x.Callee, y.Callee)
                && x.Arguments.Count == y.Arguments.Count
                && !x.Arguments.Where((e, i) => !ExpressionsEqual(e, y.Arguments[i])).Any(),
            (IndexExpr x, IndexExpr y) => ExpressionsEqual(x.Target, y.Target) && ExpressionsEqual(x.Index, y.Index),
            (AssignExpr x, AssignExpr y) =>
                x.Op == y.Op && ExpressionsEqual(x.Target, y.Target) && ExpressionsEqual(x.Value, y.Value),
            (IncDecExpr x, IncDecExpr y) =>
                x.Increment == y.Increment && x.Prefix == y.Prefix && ExpressionsEqual(x.Target, y.Target),
            _ => false,
        };
    }

    /// <summary>Whether two statements match apart from their nested blocks (same kind, same
    /// names/operands/conditions).</summary>
    private static bool HeaderEqual(Stmt a, Stmt b) => (a, b) switch
    {
        (LetStmt x, LetStmt y) =>
            string.Equals(x.Name, y.Name, StringComparison.Ordinal) && ExpressionsEqual(x.Value, y.Value),
        (DestructuringLetStmt x, DestructuringLetStmt y) =>
            x.Names.SequenceEqual(y.Names, StringComparer.Ordinal) && ExpressionsEqual(x.Value, y.Value),
        (ExprStmt x, ExprStmt y) => ExpressionsEqual(x.Expression, y.Expression),
        (IfStmt x, IfStmt y) => ExpressionsEqual(x.Condition, y.Condition),
        (WhileStmt x, WhileStmt y) => ExpressionsEqual(x.Condition, y.Condition),
        (ForStmt x, ForStmt y) =>
            string.Equals(x.Variable, y.Variable, StringComparison.Ordinal)
            && ExpressionsEqual(x.Iterable, y.Iterable),
        (FnStmt x, FnStmt y) =>
            string.Equals(x.Name, y.Name, StringComparison.Ordinal)
            && x.Parameters.SequenceEqual(y.Parameters, StringComparer.Ordinal),
        (ReturnStmt x, ReturnStmt y) => ExpressionsEqual(x.Value, y.Value),
        (BreakStmt, BreakStmt) => true,
        (ContinueStmt, ContinueStmt) => true,
        _ => false,
    };
}
