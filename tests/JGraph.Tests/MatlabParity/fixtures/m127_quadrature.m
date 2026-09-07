% m127_quadrature.m -- the quadrature above one dimension (integral2, integral3, quad2d) and the
% five names that came before it (quad, quadl, quadv, dblquad, triplequad).
%
% Two kinds of line are pinned here and they ask different questions. A *value* is pinned at
% rel=1e-10 even where the call only asked for 1e-6, because both engines run the same rule over the
% same tiles: agreeing to the tolerance would say nothing, and agreeing to ten figures says the
% tiles landed in the same places. A *count* -- quad's fcnt, quadl's, quadv's -- is pinned exact,
% because it is a statement about the method rather than about the integral: one extra bisection
% anywhere in the recursion moves it, and no tolerance hides that.
%
% quad2d's errbnd is the one quantity pinned loosely (rel=1e-2). It is a sum of several hundred
% per-tile error estimates accumulated in the order the tiles were retired, so its last figures are
% the summation's and not the method's; the tiles themselves are pinned by the value beside it.
%
% MATLAB's integral2 declares a single output, so there is no errbnd to ask it for; quad2d is the
% name that answers one.

% --- integral2: the two documented examples ----------------------------------------------------
fprintf('CHK|i2_doc|%.17g|rel=1e-10\n', integral2(@(x, y) y .* sin(x) + x .* cos(y), pi, 2 * pi, 0, pi));
fun = @(x, y) 1 ./ (sqrt(x + y) .* (1 + x + y) .^ 2);
% The integrand is infinite at (0, 0) and the region is the unit triangle: the boundary-weakening
% transform is what lets the corner be integrated rather than evaluated.
fprintf('CHK|i2_triangle|%.17g|rel=1e-10\n', integral2(fun, 0, 1, 0, @(x) 1 - x));
polarfun = @(theta, r) fun(r .* cos(theta), r .* sin(theta)) .* r;
rmax = @(theta) 1 ./ (sin(theta) + cos(theta));
fprintf('CHK|i2_polar|%.17g|rel=1e-10\n', integral2(polarfun, 0, pi / 2, 0, rmax));
fprintf('CHK|i2_unit|%.17g|rel=1e-12\n', integral2(@(x, y) x .* y, 0, 1, 0, 1));
fprintf('CHK|i2_const|%.17g|rel=1e-12\n', integral2(@(x, y) ones(size(x)), 0, 2, 0, 3));
fprintf('CHK|i2_reversed|%.17g|rel=1e-12\n', integral2(@(x, y) x .* y, 1, 0, 0, 1));

% Both limits as handles, and a region that closes to a point at one end.
fprintf('CHK|i2_wedge|%.17g|rel=1e-10\n', integral2(@(x, y) x + y, 0, 1, @(x) x .^ 2, @(x) sqrt(x)));
fprintf('CHK|i2_disc|%.17g|rel=1e-10\n', ...
    integral2(@(x, y) exp(-(x .^ 2 + y .^ 2)), -1, 1, @(x) -sqrt(1 - x .^ 2), @(x) sqrt(1 - x .^ 2)));

% A tolerance sweep: the same integral asked for at four accuracies.
gauss2 = @(x, y) exp(-x .^ 2 - y .^ 2);
fprintf('CHK|i2_tol_default|%.17g|rel=1e-10\n', integral2(gauss2, 0, 2, 0, 2));
fprintf('CHK|i2_tol_loose|%.17g|rel=1e-10\n', integral2(gauss2, 0, 2, 0, 2, 'RelTol', 1e-3));
fprintf('CHK|i2_tol_tight|%.17g|rel=1e-12\n', integral2(gauss2, 0, 2, 0, 2, 'AbsTol', 1e-12, 'RelTol', 1e-10));
fprintf('CHK|i2_tol_abs|%.17g|rel=1e-10\n', integral2(gauss2, 0, 2, 0, 2, 'AbsTol', 1e-6, 'RelTol', 0));

% The two methods named, and the one that an unbounded region forces.
fprintf('CHK|i2_tiled|%.17g|rel=1e-10\n', ...
    integral2(@(x, y) y .* sin(x) + x .* cos(y), pi, 2 * pi, 0, pi, 'Method', 'tiled'));
fprintf('CHK|i2_iterated|%.17g|rel=1e-10\n', ...
    integral2(@(x, y) y .* sin(x) + x .* cos(y), pi, 2 * pi, 0, pi, 'Method', 'iterated'));
fprintf('CHK|i2_auto|%.17g|rel=1e-10\n', ...
    integral2(@(x, y) y .* sin(x) + x .* cos(y), pi, 2 * pi, 0, pi, 'Method', 'auto'));
fprintf('CHK|i2_quarter_plane|%.17g|rel=1e-10\n', integral2(gauss2, 0, Inf, 0, Inf));
fprintf('CHK|i2_whole_plane|%.17g|rel=1e-10\n', integral2(gauss2, -Inf, Inf, -Inf, Inf));
fprintf('CHK|i2_half_strip|%.17g|rel=1e-10\n', integral2(@(x, y) exp(-x - y), 0, Inf, 0, 1));
% 'tiled' over an unbounded region is refused by name: a tile has to be a finite rectangle.
try
    integral2(gauss2, 0, Inf, 0, 1, 'Method', 'tiled');
    tiled_refused = 0;
catch
    tiled_refused = 1;
end
fprintf('CHK|i2_tiled_inf_refused|%d|exact\n', tiled_refused);

% --- quad2d: the same two examples, and the error bound beside the value ------------------------
[q, errbnd] = quad2d(@(x, y) y .* sin(x) + x .* cos(y), pi, 2 * pi, 0, pi);
fprintf('CHK|q2d_doc|%.17g|rel=1e-10\n', q);
fprintf('CHK|q2d_doc_errbnd|%.17g|rel=1e-2\n', errbnd);
[q, errbnd] = quad2d(fun, 0, 1, 0, @(x) 1 - x);
fprintf('CHK|q2d_triangle|%.17g|rel=1e-10\n', q);
fprintf('CHK|q2d_triangle_errbnd|%.17g|rel=1e-2\n', errbnd);
[q, errbnd] = quad2d(polarfun, 0, pi / 2, 0, rmax);
fprintf('CHK|q2d_polar|%.17g|rel=1e-10\n', q);
fprintf('CHK|q2d_polar_errbnd|%.17g|rel=1e-2\n', errbnd);
fprintf('CHK|q2d_one_output|%.17g|rel=1e-10\n', quad2d(@(x, y) x .* y, 0, 1, 0, 1));
% 'Singular' off is the plain product rule over x and a normalised y.
fprintf('CHK|q2d_nosingular|%.17g|rel=1e-12\n', quad2d(@(x, y) x .* y, 0, 1, 0, 1, 'Singular', false));
fprintf('CHK|q2d_nosingular_wedge|%.17g|rel=1e-10\n', ...
    quad2d(@(x, y) x + y, 0, 1, 0, @(x) 1 - x, 'Singular', false));
fprintf('CHK|q2d_reltol|%.17g|rel=1e-12\n', ...
    quad2d(@(x, y) exp(x .* y), 0, 1, 0, 1, 'RelTol', 1e-10, 'AbsTol', 1e-12));
fprintf('CHK|q2d_abstol|%.17g|rel=1e-8\n', quad2d(gauss2, 0, 2, 0, 2, 'AbsTol', 1e-3));
% MaxFunEvals is quad2d's own budget, and reaching it is a warning whose text names the number.
lastwarn('');
[q, errbnd] = quad2d(@(x, y) 1 ./ (x + y), 0, 1, 0, 1, 'AbsTol', 1e-4, 'MaxFunEvals', 3);
fprintf('CHK|q2d_maxfun_fail_msg|%s|exact\n', lastwarn);
fprintf('CHK|q2d_maxfun_fail_q|%.17g|rel=1e-10\n', q);
fprintf('CHK|q2d_maxfun_fail_errbnd|%.17g|rel=1e-2\n', errbnd);
lastwarn('');
q = quad2d(@(x, y) 1 ./ (x + y), 0, 1, 0, 1, 'AbsTol', 1e-4, 'MaxFunEvals', 4);
fprintf('CHK|q2d_maxfun_pass_msg|%s|exact\n', lastwarn);
fprintf('CHK|q2d_maxfun_pass_q|%.17g|rel=1e-10\n', q);
% The same integrand with the budget left alone runs out of rectangle instead, which is the other
% way a tiled integration stops short of its tolerance.
lastwarn('');
[q, errbnd] = quad2d(@(x, y) 1 ./ (x + y), 0, 1, 0, 1, 'AbsTol', 1e-4);
fprintf('CHK|q2d_minrect_msg|%s|exact\n', lastwarn);
fprintf('CHK|q2d_minrect_q|%.17g|rel=1e-10\n', q);
% An integrand that is not elementwise is caught by evaluating six of its points twice.
lastwarn('');
q = quad2d(@(x, y) x + y(1), 0, 1, 0, 1);
fprintf('CHK|q2d_vectorization_msg|%s|exact\n', lastwarn);

% --- integral3 ---------------------------------------------------------------------------------
f3 = @(x, y, z) y .* sin(x) + z .* cos(x);
fprintf('CHK|i3_doc|%.17g|abs=1e-9\n', integral3(f3, 0, pi, 0, 1, -1, 1));
g3 = @(x, y, z) x .* cos(y) + x .^ 2 .* cos(z);
fprintf('CHK|i3_sphere|%.17g|rel=1e-10\n', integral3(g3, -1, 1, @(x) -sqrt(1 - x .^ 2), @(x) sqrt(1 - x .^ 2), ...
    @(x, y) -sqrt(1 - x .^ 2 - y .^ 2), @(x, y) sqrt(1 - x .^ 2 - y .^ 2)));
fprintf('CHK|i3_box|%.17g|rel=1e-12\n', integral3(@(x, y, z) x .* y .* z, 0, 1, 0, 2, 0, 3));
fprintf('CHK|i3_tetra|%.17g|rel=1e-10\n', ...
    integral3(@(x, y, z) ones(size(x)), 0, 1, 0, @(x) 1 - x, 0, @(x, y) 1 - x - y));
fprintf('CHK|i3_tol|%.17g|rel=1e-12\n', ...
    integral3(@(x, y, z) exp(-x - y - z), 0, 1, 0, 1, 0, 1, 'AbsTol', 1e-12, 'RelTol', 1e-10));
fprintf('CHK|i3_iterated|%.17g|abs=1e-9\n', integral3(f3, 0, pi, 0, 1, -1, 1, 'Method', 'iterated'));
fprintf('CHK|i3_inf_z|%.17g|rel=1e-10\n', integral3(@(x, y, z) exp(-x - y - z), 0, 1, 0, 1, 0, Inf));
fprintf('CHK|i3_inf_x|%.17g|rel=1e-10\n', integral3(@(x, y, z) exp(-x - y - z), 0, Inf, 0, 1, 0, 1));

% --- quad: adaptive Simpson, its value and its evaluation count ---------------------------------
[q, fcnt] = quad(@(x) 1 ./ (x .^ 3 - 2 * x - 5), 0, 2);
fprintf('CHK|quad_doc|%.17g|rel=1e-12\n', q);
fprintf('CHK|quad_doc_fcnt|%d|exact\n', fcnt);
[q, fcnt] = quad(@(x) sin(x), 0, pi);
fprintf('CHK|quad_sin|%.17g|rel=1e-12\n', q);
fprintf('CHK|quad_sin_fcnt|%d|exact\n', fcnt);
[q, fcnt] = quad(@(x) x .^ 2, 0, 1, 1e-10);
fprintf('CHK|quad_tol|%.17g|rel=1e-12\n', q);
fprintf('CHK|quad_tol_fcnt|%d|exact\n', fcnt);
fprintf('CHK|quad_tol_empty|%.17g|rel=1e-12\n', quad(@(x) x .^ 2, 0, 1, []));
% trace is a fifth argument that prints as it recurses; zero is the documented off.
fprintf('CHK|quad_trace_off|%.17g|rel=1e-12\n', quad(@(x) x .^ 2, 0, 1, [], 0));
% Extra arguments after trace are handed to the integrand, which is how dblquad passes a fixed y.
fprintf('CHK|quad_extra|%.17g|rel=1e-12\n', quad(@(x, c) 1 ./ (x .^ 3 - 2 * x - c), 0, 2, [], [], 5));
% A singularity on a limit: the endpoint is nudged in by one ulp and the recursion warns.
lastwarn('');
[q, fcnt] = quad(@(x) 1 ./ sqrt(x), 0, 1);
fprintf('CHK|quad_sing_msg|%s|exact\n', lastwarn);
fprintf('CHK|quad_sing|%.17g|rel=1e-10\n', q);
fprintf('CHK|quad_sing_fcnt|%d|exact\n', fcnt);

% --- quadl: adaptive Lobatto over the same integrands -------------------------------------------
[q, fcnt] = quadl(@(x) 1 ./ (x .^ 3 - 2 * x - 5), 0, 2);
fprintf('CHK|quadl_doc|%.17g|rel=1e-12\n', q);
fprintf('CHK|quadl_doc_fcnt|%d|exact\n', fcnt);
[q, fcnt] = quadl(@(x) exp(-x .^ 2), 0, 3, 1e-10);
fprintf('CHK|quadl_gauss|%.17g|rel=1e-12\n', q);
fprintf('CHK|quadl_gauss_fcnt|%d|exact\n', fcnt);
fprintf('CHK|quadl_trace_off|%.17g|rel=1e-12\n', quadl(@(x) x .^ 2, 0, 1, [], 0));
fprintf('CHK|quadl_extra|%.17g|rel=1e-12\n', quadl(@(x, c) 1 ./ (x .^ 3 - 2 * x - c), 0, 2, [], [], 5));
lastwarn('');
[q, fcnt] = quadl(@(x) 1 ./ sqrt(x), 0, 1);
fprintf('CHK|quadl_sing_msg|%s|exact\n', lastwarn);
fprintf('CHK|quadl_sing|%.17g|rel=1e-10\n', q);
fprintf('CHK|quadl_sing_fcnt|%d|exact\n', fcnt);

% --- quadv: the array-valued Simpson ------------------------------------------------------------
[Q, fcnt] = quadv(@(x) 1 ./ ((1:10) + x), 0, 1);
fprintf('CHK|quadv_shape|%s|shape\n', mat2str(size(Q)));
fprintf('CHK|quadv_1|%.17g|rel=1e-12\n', Q(1));
fprintf('CHK|quadv_5|%.17g|rel=1e-12\n', Q(5));
fprintf('CHK|quadv_10|%.17g|rel=1e-12\n', Q(10));
fprintf('CHK|quadv_fcnt|%d|exact\n', fcnt);
% The one tolerance is applied to the largest component, so the answers are not what ten separate
% quad calls would give -- which is the note MATLAB's own help puts on this name.
[Q, fcnt] = quadv(@(x) [sin(x), cos(x), x .^ 3], 0, pi, 1e-9);
fprintf('CHK|quadv_tol_1|%.17g|rel=1e-11\n', Q(1));
fprintf('CHK|quadv_tol_2|%.17g|abs=1e-9\n', Q(2));
fprintf('CHK|quadv_tol_3|%.17g|rel=1e-11\n', Q(3));
fprintf('CHK|quadv_tol_fcnt|%d|exact\n', fcnt);
fprintf('CHK|quadv_extra|%.17g|rel=1e-12\n', sum(quadv(@(x, n) 1 ./ ((1:n) + x), 0, 1, [], [], 3)));

% --- dblquad and triplequad: the legacy nesting over quad ---------------------------------------
fprintf('CHK|dbl_doc|%.17g|rel=1e-12\n', dblquad(@(x, y) y .* sin(x) + x .* cos(y), pi, 2 * pi, 0, pi));
fprintf('CHK|dbl_tol|%.17g|rel=1e-12\n', ...
    dblquad(@(x, y) y .* sin(x) + x .* cos(y), pi, 2 * pi, 0, pi, 1e-8));
fprintf('CHK|dbl_quadl|%.17g|rel=1e-12\n', ...
    dblquad(@(x, y) y .* sin(x) + x .* cos(y), pi, 2 * pi, 0, pi, 1e-8, @quadl));
% A region that is not a rectangle, handled the way the help says: zero outside it.
fprintf('CHK|dbl_hemisphere|%.17g|rel=1e-10\n', ...
    dblquad(@(x, y) sqrt(max(1 - (x .^ 2 + y .^ 2), 0)), -1, 1, -1, 1));
fprintf('CHK|dbl_masked|%.17g|rel=1e-10\n', ...
    dblquad(@(x, y) sqrt(1 - (x .^ 2 + y .^ 2)) .* (x .^ 2 + y .^ 2 <= 1), -1, 1, -1, 1));
fprintf('CHK|tri_doc|%.17g|rel=1e-12\n', ...
    triplequad(@(x, y, z) y .* sin(x) + z .* cos(x), 0, pi, 0, 1, -1, 1));
fprintf('CHK|tri_tol|%.17g|rel=1e-12\n', triplequad(@(x, y, z) x .* y .* z, 0, 1, 0, 2, 0, 3, 1e-8));
fprintf('CHK|tri_quadl|%.17g|rel=1e-12\n', ...
    triplequad(@(x, y, z) x .* y .* z, 0, 1, 0, 2, 0, 3, 1e-8, @quadl));
% [] in the tolerance slot asks for the default, and the method slot may then still be named.
fprintf('CHK|dbl_tol_empty|%.17g|rel=1e-12\n', ...
    dblquad(@(x, y) y .* sin(x) + x .* cos(y), pi, 2 * pi, 0, pi, []));
fprintf('CHK|dbl_tol_empty_quadl|%.17g|rel=1e-12\n', ...
    dblquad(@(x, y) y .* sin(x) + x .* cos(y), pi, 2 * pi, 0, pi, [], @quadl));
fprintf('CHK|tri_tol_empty|%.17g|rel=1e-12\n', ...
    triplequad(@(x, y, z) x .* y .* z, 0, 1, 0, 2, 0, 3, []));

% quad2d's remaining documented option: the failure plot, which only draws when the budget runs out
% and so is asked for here on an integral that finishes.
fprintf('CHK|q2d_failureplot|%.17g|rel=1e-12\n', ...
    quad2d(@(x, y) x .* y, 0, 1, 0, 1, 'FailurePlot', false));
fprintf('CHK|q2d_all_options|%.17g|rel=1e-10\n', ...
    quad2d(gauss2, 0, 2, 0, 2, 'AbsTol', 1e-8, 'RelTol', 1e-8, 'Singular', true, ...
    'MaxFunEvals', 2000, 'FailurePlot', false));
