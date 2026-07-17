namespace JGraph.Scripting.Jgs;

/// <summary>
/// Uniform access to a statement's nested blocks ("slots"), so the debugger can walk the statement
/// tree without knowing every node type: an <c>if</c> has slot 0 (then) and slot 1 (else); loops and
/// function declarations have slot 0 (body); everything else has none.
/// </summary>
internal static class AstChildren
{
    /// <summary>How many block slots <paramref name="statement"/> has (present or not — an
    /// <c>if</c> reports 2 even without an else branch).</summary>
    public static int SlotCount(Stmt statement) => statement switch
    {
        IfStmt => 2,
        WhileStmt or ForStmt or FnStmt => 1,
        _ => 0,
    };

    /// <summary>The block in <paramref name="slot"/> of <paramref name="statement"/>, or null when
    /// the slot does not exist (or is an absent else branch).</summary>
    public static IReadOnlyList<Stmt>? Slot(Stmt statement, int slot) => (statement, slot) switch
    {
        (IfStmt s, 0) => s.Then,
        (IfStmt s, 1) => s.Else,
        (WhileStmt s, 0) => s.Body,
        (ForStmt s, 0) => s.Body,
        (FnStmt s, 0) => s.Body,
        _ => null,
    };

    /// <summary>Which slot of <paramref name="statement"/> is <paramref name="child"/> (by reference),
    /// or null when the list is not one of its blocks.</summary>
    public static int? SlotOf(Stmt statement, IReadOnlyList<Stmt> child)
    {
        for (int slot = 0; slot < SlotCount(statement); slot++)
        {
            if (ReferenceEquals(Slot(statement, slot), child))
            {
                return slot;
            }
        }

        return null;
    }
}
