namespace JGraph.Scripting.Jgs;

/// <summary>
/// V6 (ADR 0167): <c>S(…) = v</c> on a sparse matrix. The three index-write roads hand a sparse
/// target here; the subscripts are evaluated once against the matrix's extents, the entries are
/// rebuilt (<see cref="JgsBuiltins.SparseAssigned"/>), and the new matrix is stored back through
/// the target's own entry — a variable, a field, a cell element — so an alias taken before the
/// write keeps the matrix it had. A sparse matrix is immutable, so there is nothing to detach and
/// a refused write has changed nothing (M14).
/// </summary>
internal sealed partial class Interpreter
{
    private JgsValue AssignIntoSparse(
        Expr target, JgsValue held, IReadOnlyList<Expr> subscripts, TokenType op, JgsValue rhs, Node at,
        JgsEnvironment env)
    {
        var matrix = held.AsSparse;
        if (subscripts.Count > 2)
        {
            throw new JgsRuntimeException(at.Line, at.Column, "N-dimensional indexing allowed for full matrices only.");
        }

        int[] extents = subscripts.Count == 1
            ? [checked(matrix.Rows * matrix.Cols)]
            : [matrix.Rows, matrix.Cols];
        var indices = new JgsValue?[subscripts.Count];
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = EvaluateIndexArgument(subscripts[i], extents, i, env);
        }

        if (op != TokenType.Assign)
        {
            var read = new JgsValue[indices.Length];
            for (int i = 0; i < read.Length; i++)
            {
                read[i] = indices[i] ?? JgsMatrix.FromColumnMajor(
                    AllPicks(extents[i]).Select(n => (double)(n + Dialect.IndexBase)).ToArray(), extents[i], 1);
            }

            rhs = ApplyBinary(UnderlyingOp(op),
                JgsBuiltins.SparseSubscript(held, read, Dialect, at.Line, at.Column), rhs, at);
        }

        var rebuilt = JgsBuiltins.SparseAssigned(matrix, indices, rhs, Dialect, at.Line, at.Column);
        StoreBack(target, JgsValue.Sparse(rebuilt), at, env);
        return rhs;
    }
}
