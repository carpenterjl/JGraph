namespace JGraph.Scripting.Jgs;

/// <summary>
/// The interpreter's debug seam. A hooked interpreter calls <see cref="BeforeStatement"/> before every
/// statement, brackets every block with <see cref="EnterBlock"/>/<see cref="ExitBlock"/> and each
/// user-function call with <see cref="EnterFunction"/>/<see cref="ExitFunction"/>. The hook runs on the
/// interpreter thread and may block (that is how the debugger pauses); a null hook costs one null check
/// per statement, keeping plain runs at full speed.
/// </summary>
internal interface IJgsDebugHook
{
    /// <summary>
    /// Called once, before any statement of the run executes, so the hook can reach the run's
    /// interpreter and global scope (live edits re-hoist functions through them).
    /// </summary>
    /// <param name="interpreter">The interpreter about to execute the program.</param>
    /// <param name="globals">The run's global scope, seeded with the built-ins.</param>
    void RunStarting(Interpreter interpreter, JgsEnvironment globals);

    /// <summary>
    /// Called before the statement at <paramref name="index"/> of <paramref name="block"/> executes.
    /// May block to pause execution. Returns the index to jump to instead (set-next-statement),
    /// or null to execute <paramref name="index"/> normally.
    /// </summary>
    /// <param name="block">The block being executed (its statement list may be edited while paused).</param>
    /// <param name="index">The index of the statement about to execute.</param>
    /// <param name="env">The environment the statement executes in (safe to read only while blocked).</param>
    /// <param name="callDepth">The current user-function call depth (0 at top level).</param>
    int? BeforeStatement(BlockExecution block, int index, JgsEnvironment env, int callDepth);

    /// <summary>Called when a block starts executing, before its first <see cref="BeforeStatement"/> —
    /// this is how the hook knows the chain of blocks execution is inside (the live-edit path).</summary>
    /// <param name="block">The cursor over the block's statements.</param>
    void EnterBlock(BlockExecution block);

    /// <summary>Called when the innermost block finishes (normally or by unwinding).</summary>
    void ExitBlock();

    /// <summary>Called when a user function is entered, before its body block.</summary>
    /// <param name="declaration">The function being invoked (name, parameters, body, source).</param>
    /// <param name="callLine">The 1-based line of the call site.</param>
    /// <param name="local">The function's local environment (parameters already bound).</param>
    void EnterFunction(FnStmt declaration, int callLine, JgsEnvironment local);

    /// <summary>Called when the innermost user function exits (normally or by unwinding).</summary>
    void ExitFunction();
}

/// <summary>
/// A read cursor over the statement list a block is executing. The list reference is shared with the
/// owning AST node, and the interpreter re-reads it on every step — so a live edit that mutates the
/// underlying list in place (only while the interpreter thread is blocked inside
/// <see cref="IJgsDebugHook.BeforeStatement"/>) takes effect immediately, including on later loop
/// iterations and in closures that captured the enclosing function.
/// </summary>
internal sealed class BlockExecution
{
    /// <summary>Creates the cursor over <paramref name="statements"/>.</summary>
    public BlockExecution(IReadOnlyList<Stmt> statements) => Statements = statements;

    /// <summary>The statements the block is executing (a live view of the AST node's list).</summary>
    public IReadOnlyList<Stmt> Statements { get; }
}
