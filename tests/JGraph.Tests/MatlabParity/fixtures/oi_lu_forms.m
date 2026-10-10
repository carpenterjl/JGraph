% Open item 5 (ADR 0221): lu's one output is LAPACK's own matrix, L - I + U with the permutation
% dropped, so its triangles are the three-output factors bit for bit; the two-output L is P'*L bit for
% bit, over sizes 1 to 257 and a rank-deficient matrix. Probe probe_5 (open-items scratch). The
% factors themselves are compared only against each other: the two linear algebra lanes and R2025b's
% MKL may round differently.

for n = [1 2 3 7 64 257]
    A = mod((1:n)' * (1:n) + 3 * (1:n)', 11) - 5 + eye(n);
    if n >= 7
        A(:, 3) = A(:, 2);
    end
    [L, U, P] = lu(A);
    [L2, U2] = lu(A);
    Y = lu(A);
    tag = sprintf('n%d', n);
    u9b_chk(['two_output_lower_' tag], @() isequal(L2, P' * L));
    u9b_chk(['two_output_upper_' tag], @() isequal(U2, U));
    u9b_chk(['one_output_lower_' tag], @() isequal(tril(Y, -1), tril(L, -1)));
    u9b_chk(['one_output_upper_' tag], @() isequal(triu(Y), U));
end

A = [1 2 3; 4 5 6; 7 8 10];
Y = lu(A);
[L, U, P] = lu(A);
u9b_chk('one_output_is_not_permuted', @() isequal(Y, P' * L + U - eye(3)));
u9b_chk('one_output_diagonal_is_u', @() isequal(diag(Y), diag(U)));
u9b_chk('one_output_shape', @() size(Y));
