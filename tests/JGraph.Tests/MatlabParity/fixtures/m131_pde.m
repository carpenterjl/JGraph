% m131_pde.m -- pdepe and pdeval, and the legacy expression helpers that share funfun with them:
% symvar, vectorize, inline and fcnchk.
%
% The five problems are the ones the documentation's mini-tutorial is built on, rewritten as local
% functions: a slab with one parabolic equation and a flux boundary condition, a spherical problem
% whose mesh starts on the axis, a slab whose ends are both prescribed, a two-component system with
% one Dirichlet and one Neumann end apiece, and a two-component system integrated to t = 200. Each
% one exercises a different corner of the discretisation -- the singular interpolant, the algebraic
% rows a q = 0 boundary condition makes, the block-tridiagonal Jacobian of a system.
%
% The cost is pinned as the number of times the problem's own coefficient function is called, which
% a persistent counter in this file records. It is the discretisation's evaluation count -- one call
% per subinterval per right-hand side, plus the numerical Jacobian's columns -- and it moves the
% moment a step is taken differently, so it is the same kind of claim m125 and m126 make with
% nsteps. Every one of the five holds exactly.
%
% The expression helpers are pinned exact, text and all. symvar's rules are the fiddly part: a name
% followed by '(' is a function, blanks before that parenthesis do not save it, a name after '.' is
% a field or the tail of 1.e10, a quote after a name is a transpose and not a string, and the
% filtered constants are case-sensitive so that I and J survive where i and j do not.

% --- pdex1: c = pi^2, f = du/dx, s = 0 on [0, 1], u(0) = 0 and a flux condition at x = 1 --------
x1 = linspace(0, 1, 20);
t1 = linspace(0, 2, 5);
evals(-1);
sol1 = pdepe(0, @pdex1pde, @pdex1ic, @pdex1bc, x1, t1);
n1 = evals(0);
fprintf('CHK|pdex1_size|%s|shape\n', mat2str(size(sol1)));
fprintf('CHK|pdex1_evals|%d|exact\n', n1);
u1 = sol1(:, :, 1);
for k = [2 5 10 15 20]
    fprintf('CHK|pdex1_last_%d|%.17g|rel=1e-9\n', k, u1(end, k));
end
fprintf('CHK|pdex1_mid_5|%.17g|rel=1e-9\n', u1(3, 5));
fprintf('CHK|pdex1_first_row_max|%.17g|rel=1e-12\n', max(u1(1, :)));
[uo1, do1] = pdeval(0, x1, u1(end, :), [0 0.25 0.5 0.75 1]);
fprintf('CHK|pdex1_pdeval_shape|%s|shape\n', mat2str(size(uo1)));
for k = 1:5
    fprintf('CHK|pdex1_pdeval_u_%d|%.17g|rel=1e-9\n', k, uo1(k));
    fprintf('CHK|pdex1_pdeval_dudx_%d|%.17g|rel=1e-9\n', k, do1(k));
end

% --- pdex1 again with an event: the solution at one mesh point falling through a half ----------
opts1 = odeset('Events', @pdex1event);
[sole1, tsol1, sole1v, te1, ie1] = pdepe(0, @pdex1pde, @pdex1ic, @pdex1bc, x1, t1, opts1);
fprintf('CHK|pdex1_event_tsol_n|%d|exact\n', numel(tsol1));
fprintf('CHK|pdex1_event_tsol_last|%.17g|rel=1e-12\n', tsol1(end));
fprintf('CHK|pdex1_event_te_n|%d|exact\n', numel(te1));
fprintf('CHK|pdex1_event_te|%.17g|rel=1e-9\n', te1(1));
fprintf('CHK|pdex1_event_ie|%d|exact\n', ie1(1));
fprintf('CHK|pdex1_event_sole_shape|%s|shape\n', mat2str(size(sole1v)));
fprintf('CHK|pdex1_event_sole_10|%.17g|rel=1e-9\n', sole1v(1, 10, 1));
fprintf('CHK|pdex1_event_sol_shape|%s|shape\n', mat2str(size(sole1)));

% --- pdex2: spherical symmetry with the axis in the mesh, and both ends algebraic ---------------
x2 = [0 0.1 0.2 0.3 0.4 0.45 0.475 0.5 0.525 0.55 0.6 0.7 0.8 0.9 0.95 0.975 0.99 1];
t2 = [0 0.001 0.005 0.01 0.05 0.1 0.5 1];
evals(-1);
sol2 = pdepe(2, @pdex2pde, @pdex2ic, @pdex2bc, x2, t2);
n2 = evals(0);
fprintf('CHK|pdex2_size|%s|shape\n', mat2str(size(sol2)));
fprintf('CHK|pdex2_evals|%d|exact\n', n2);
u2 = sol2(:, :, 1);
for k = [1 4 8 12 18]
    fprintf('CHK|pdex2_last_%d|%.17g|rel=1e-9\n', k, u2(end, k));
end
fprintf('CHK|pdex2_row4_8|%.17g|rel=1e-9\n', u2(4, 8));
fprintf('CHK|pdex2_right_end|%.17g|rel=1e-12\n', u2(end, end));
[uo2, do2] = pdeval(2, x2, u2(end, :), [0.05 0.25 0.49 0.75 1]);
for k = 1:5
    fprintf('CHK|pdex2_pdeval_u_%d|%.17g|rel=1e-9\n', k, uo2(k));
    fprintf('CHK|pdex2_pdeval_dudx_%d|%.17g|rel=1e-9\n', k, do2(k));
end

% --- pdex3: a drift-diffusion slab whose discharge current is du/dx at the left end -------------
x3 = linspace(0, 1, 41);
t3 = linspace(0, 1, 51);
evals(-1);
sol3 = pdepe(0, @pdex3pde, @pdex3ic, @pdex3bc, x3, t3);
n3 = evals(0);
fprintf('CHK|pdex3_size|%s|shape\n', mat2str(size(sol3)));
fprintf('CHK|pdex3_evals|%d|exact\n', n3);
u3 = sol3(:, :, 1);
for k = [2 11 21 31 40]
    fprintf('CHK|pdex3_last_%d|%.17g|rel=1e-8\n', k, u3(end, k));
end
for j = [2 11 26 51]
    [~, current] = pdeval(0, x3, u3(j, :), 0);
    fprintf('CHK|pdex3_current_%d|%.17g|rel=1e-8\n', j, current);
end

% --- pdex4: two coupled components, each end Dirichlet in one and Neumann in the other ---------
x4 = [0 0.005 0.01 0.05 0.1 0.2 0.5 0.7 0.9 0.95 0.99 0.995 1];
t4 = [0 0.005 0.01 0.05 0.1 0.5 1 1.5 2];
evals(-1);
sol4 = pdepe(0, @pdex4pde, @pdex4ic, @pdex4bc, x4, t4);
n4 = evals(0);
fprintf('CHK|pdex4_size|%s|shape\n', mat2str(size(sol4)));
fprintf('CHK|pdex4_evals|%d|exact\n', n4);
for k = [1 5 9 13]
    fprintf('CHK|pdex4_u1_%d|%.17g|rel=1e-9\n', k, sol4(end, k, 1));
    fprintf('CHK|pdex4_u2_%d|%.17g|rel=1e-9\n', k, sol4(end, k, 2));
end
fprintf('CHK|pdex4_u1_row5|%.17g|rel=1e-9\n', sol4(5, 7, 1));
fprintf('CHK|pdex4_u2_row5|%.17g|rel=1e-9\n', sol4(5, 7, 2));
[uo4, do4] = pdeval(0, x4, sol4(end, :, 2), [0.02 0.3 0.8]);
for k = 1:3
    fprintf('CHK|pdex4_pdeval_u_%d|%.17g|rel=1e-9\n', k, uo4(k));
    fprintf('CHK|pdex4_pdeval_dudx_%d|%.17g|rel=1e-9\n', k, do4(k));
end

% --- pdex5: chemotaxis to t = 200, both ends pure Neumann so nothing is algebraic --------------
x5 = linspace(0, 1, 41);
t5 = linspace(0, 200, 10);
evals(-1);
sol5 = pdepe(0, @pdex5pde, @pdex5ic, @pdex5bc, x5, t5);
n5 = evals(0);
fprintf('CHK|pdex5_size|%s|shape\n', mat2str(size(sol5)));
fprintf('CHK|pdex5_evals|%d|exact\n', n5);
for k = [1 11 21 31 41]
    fprintf('CHK|pdex5_n_%d|%.17g|rel=1e-8\n', k, sol5(end, k, 1));
    fprintf('CHK|pdex5_c_%d|%.17g|rel=1e-8\n', k, sol5(end, k, 2));
end
fprintf('CHK|pdex5_n_row3|%.17g|rel=1e-8\n', sol5(3, 15, 1));
fprintf('CHK|pdex5_c_row3|%.17g|rel=1e-8\n', sol5(3, 15, 2));

% --- what pdepe and pdeval refuse --------------------------------------------------------------
try
    pdepe(3, @pdex1pde, @pdex1ic, @pdex1bc, x1, t1);
    fprintf('CHK|pdepe_bad_m|accepted|exact\n');
catch
    fprintf('CHK|pdepe_bad_m|refused|exact\n');
end
try
    pdepe(0, @pdex1pde, @pdex1ic, @pdex1bc, x1, [0 1]);
    fprintf('CHK|pdepe_short_tspan|accepted|exact\n');
catch
    fprintf('CHK|pdepe_short_tspan|refused|exact\n');
end
try
    pdepe(0, @pdex1pde, @pdex1ic, @pdex1bc, [0 1], t1);
    fprintf('CHK|pdepe_short_xmesh|accepted|exact\n');
catch
    fprintf('CHK|pdepe_short_xmesh|refused|exact\n');
end
try
    pdeval(0, x1, u1(end, :), 1.5);
    fprintf('CHK|pdeval_outside|accepted|exact\n');
catch
    fprintf('CHK|pdeval_outside|refused|exact\n');
end

% --- symvar: what counts as a variable in a string expression ----------------------------------
names = {'cos(pi*x - beta1)', 'x^2+y', '2', 'sin(2*pi*f + theta)', ...
    'a*b + f(1) + i + j + pi + eps + Inf + NaN', 'str.fname + 1.e10 + q', ...
    'foo (x) + bar', 'a''+b', 'x + ''hello y'' + z', '_a + a_1 + A + a', ...
    'exp(-t)*sin(pi*x)', 'inf+nan+Inf+NaN+eps+I+J'};
for k = 1:numel(names)
    got = symvar(names{k});
    fprintf('CHK|symvar_%d|%s|exact\n', k, strjoin(got(:)', ','));
    fprintf('CHK|symvar_%d_n|%d|exact\n', k, numel(got));
end
empty = symvar('');
fprintf('CHK|symvar_empty_n|%d|exact\n', numel(empty));
fprintf('CHK|symvar_empty_class|%s|exact\n', class(empty));

% --- vectorize: a dot before every *, / and ^, and none doubled --------------------------------
forms = {'x^2', 'a*b/c^d', 'a.*b./c.^d', 'x**y', '3*x + 2'};
for k = 1:numel(forms)
    fprintf('CHK|vectorize_%d|%s|exact\n', k, vectorize(forms{k}));
end
fprintf('CHK|vectorize_empty_n|%d|exact\n', numel(vectorize('')));

% --- inline: the formula, the argument names, and calling it ------------------------------------
f = inline('x^2+y', 'x', 'y');
fprintf('CHK|inline_formula|%s|exact\n', formula(f));
fprintf('CHK|inline_args|%s|exact\n', strjoin(argnames(f)', ','));
fprintf('CHK|inline_char|%s|exact\n', char(f));
fprintf('CHK|inline_call|%.17g|exact\n', f(1, 2));
fprintf('CHK|inline_nargs|%d|exact\n', numel(argnames(f)));
g = inline('  sin(2*pi*f + theta) ');
fprintf('CHK|inline_derived_formula|%s|exact\n', formula(g));
fprintf('CHK|inline_derived_args|%s|exact\n', strjoin(argnames(g)', ','));
fprintf('CHK|inline_derived_call|%.17g|rel=1e-15\n', g(0.25, 0));
h = inline('x^P1', 1);
fprintf('CHK|inline_numbered_formula|%s|exact\n', formula(h));
fprintf('CHK|inline_numbered_args|%s|exact\n', strjoin(argnames(h)', ','));
fprintf('CHK|inline_numbered_call|%.17g|exact\n', h(2, 3));
k0 = inline('2');
fprintf('CHK|inline_constant_args|%s|exact\n', strjoin(argnames(k0)', ','));
fprintf('CHK|inline_constant_call|%.17g|exact\n', k0(9));
vf = vectorize(inline('x^2*y'));
fprintf('CHK|inline_vectorized|%s|exact\n', formula(vf));
fprintf('CHK|inline_vectorized_call|%.17g|exact\n', vf(3, 2));
% An inline is an anonymous function here and an object of class inline in MATLAB, so class and
% the display that names the class part company. Everything else about it agrees.
fprintf('CHK|inline_class|%s|div=ADR0131\n', class(f));

% --- fcnchk: a handle from a name, an expression, or a handle ----------------------------------
q1 = fcnchk('sin');
fprintf('CHK|fcnchk_name_class|%s|exact\n', class(q1));
fprintf('CHK|fcnchk_name_call|%.17g|rel=1e-15\n', q1(0.5));
q2 = fcnchk('x^2+y');
fprintf('CHK|fcnchk_expr_formula|%s|exact\n', formula(q2));
fprintf('CHK|fcnchk_expr_args|%s|exact\n', strjoin(argnames(q2)', ','));
fprintf('CHK|fcnchk_expr_call|%.17g|exact\n', q2(2, 3));
q3 = fcnchk(@(x) x + 1);
fprintf('CHK|fcnchk_handle_class|%s|exact\n', class(q3));
fprintf('CHK|fcnchk_handle_call|%.17g|exact\n', q3(4));
q4 = fcnchk('x^2', 'vectorized');
fprintf('CHK|fcnchk_vec_formula|%s|exact\n', formula(q4));
fprintf('CHK|fcnchk_vec_args|%s|exact\n', strjoin(argnames(q4)', ','));
fprintf('CHK|fcnchk_vec_call|%.17g|exact\n', sum(q4([1 2 3])));
q5 = fcnchk('x*y', 'x', 'y', 'vectorized');
fprintf('CHK|fcnchk_vec2_formula|%s|exact\n', formula(q5));
fprintf('CHK|fcnchk_vec2_args|%s|exact\n', strjoin(argnames(q5)', ','));
q6 = fcnchk('');
fprintf('CHK|fcnchk_empty_formula|%s|exact\n', formula(q6));

% --- inlineeval: the helper @inline/subsref evaluates a formula through ------------------------
fprintf('CHK|inlineeval|%.17g|exact\n', inlineeval({3, 4}, ' a = INLINE_INPUTS_{1}; b = INLINE_INPUTS_{2};', 'a^2 + b'));

% ============================================================================================

function out = evals(command)
% A persistent count of the coefficient-function calls one pdepe run makes: -1 resets it, 0 reads
% it, and anything else adds one.
persistent total
if isempty(total)
    total = 0;
end
if command == -1
    total = 0;
elseif command ~= 0
    total = total + 1;
end
if nargout > 0
    out = total;
end
end

function [c, f, s] = pdex1pde(x, t, u, DuDx) %#ok<INUSL>
evals(1);
c = pi^2;
f = DuDx;
s = 0;
end

function u0 = pdex1ic(x)
u0 = sin(pi * x);
end

function [pl, ql, pr, qr] = pdex1bc(xl, ul, xr, ur, t) %#ok<INUSD>
pl = ul;
ql = 0;
pr = pi * exp(-t);
qr = 1;
end

function [value, isterminal, direction] = pdex1event(m, t, xmesh, umesh) %#ok<INUSL>
value = umesh(10) - 0.5;
isterminal = 1;
direction = -1;
end

function [c, f, s] = pdex2pde(x, t, u, DuDx) %#ok<INUSL>
evals(1);
c = 1;
if x <= 0.5
    f = 5 * DuDx;
    s = -1000 * exp(u);
else
    f = DuDx;
    s = -exp(u);
end
end

function u0 = pdex2ic(x)
if x < 1
    u0 = 0;
else
    u0 = 1;
end
end

function [pl, ql, pr, qr] = pdex2bc(xl, ul, xr, ur, t) %#ok<INUSD>
pl = 0;
ql = 0;
pr = ur - 1;
qr = 0;
end

function [c, f, s] = pdex3pde(x, t, u, DuDx) %#ok<INUSL>
evals(1);
c = 1;
f = 0.1 * DuDx;
s = -0.1 * 10 * DuDx;
end

function u0 = pdex3ic(x)
u0 = (0.1 * 1 / 0.1) * (1 - exp(-10 * (1 - x))) / 10;
end

function [pl, ql, pr, qr] = pdex3bc(xl, ul, xr, ur, t) %#ok<INUSD>
pl = ul;
ql = 0;
pr = ur;
qr = 0;
end

function [c, f, s] = pdex4pde(x, t, u, DuDx) %#ok<INUSL>
evals(1);
c = [1; 1];
f = [0.024; 0.17] .* DuDx;
y = u(1) - u(2);
F = exp(5.73 * y) - exp(-11.47 * y);
s = [-F; F];
end

function u0 = pdex4ic(x) %#ok<INUSD>
u0 = [1; 0];
end

function [pl, ql, pr, qr] = pdex4bc(xl, ul, xr, ur, t) %#ok<INUSD>
pl = [0; ul(2)];
ql = [1; 0];
pr = [ur(1) - 1; 0];
qr = [0; 1];
end

function [c, f, s] = pdex5pde(x, t, u, DuDx) %#ok<INUSL>
evals(1);
d = 1e-3;
a = 3.8;
S = 3;
r = 0.88;
N = 1;
c = [1; 1];
f = [d * DuDx(1) - a * u(1) * DuDx(2); DuDx(2)];
s = [S * r * u(1) * (N - u(1)); S * (u(1) / (u(1) + 1) - u(2))];
end

function u0 = pdex5ic(x)
u0 = [1; 0.5];
if x >= 0.3 && x <= 0.6
    u0(1) = 1.05 * u0(1);
    u0(2) = 1.0005 * u0(2);
end
end

function [pl, ql, pr, qr] = pdex5bc(xl, ul, xr, ur, t) %#ok<INUSD>
pl = [0; 0];
ql = [1; 1];
pr = [0; 0];
qr = [1; 1];
end
