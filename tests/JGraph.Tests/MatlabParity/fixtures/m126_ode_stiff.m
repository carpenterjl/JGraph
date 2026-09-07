% m126_ode_stiff.m -- the stiff family (ode15s, ode23s, ode23t, ode23tb) and the fully implicit
% pair (ode15i, decic), pinned by what each solver does and what it answers. A stiff solver's cost
% is not its evaluations but its Jacobians, its factorizations and its solves, so nsteps, nfailed,
% nfevals, npds, ndecomps and nsolves are all pinned exact wherever they are exact: an iteration
% matrix refreshed one step early, a Newton test with a different constant, or a numerical Jacobian
% with a different increment shows as a count off by one that no tolerance hides.
%
% Two runs are pinned within two per cent instead, and both are named where they appear: ode23s on
% Robertson and ode15i on the implicit Robertson track R2025b step for step for hundreds of steps
% and then part, because a numerically differenced Jacobian steers each step from an error estimate
% whose last bits are rounding. Both agree exactly under an analytic Jacobian, which is what says
% the difference is arithmetic rather than algorithm.
stiff = {'ode15s', 'ode23s', 'ode23t', 'ode23tb'};

% --- van der Pol at mu = 1000: the problem the family exists for --------------------------------
vdp1000 = @(t, y) [y(2); 1000 * (1 - y(1)^2) * y(2) - y(1)];
for i = 1:4
    name = stiff{i};
    f = str2func(name);
    sol = f(vdp1000, [0 3000], [2; 0]);
    % ode23s forms a fresh numerical Jacobian every step, so it is the one solver here whose
    % evaluation count and answer carry the differencing's own rounding: over seven hundred and
    % forty steps of a relaxation oscillator the two engines take the same steps and part in the
    % fourth figure, which is the accuracy the default tolerance asked for.
    if strcmp(name, 'ode23s')
        evrule = 'rel=2e-2';
        valrule = 'rel=1e-3';
    else
        evrule = 'exact';
        valrule = 'rel=1e-6';
    end
    fprintf('CHK|%s_vdp_nsteps|%d|exact\n', name, sol.stats.nsteps);
    fprintf('CHK|%s_vdp_nfailed|%d|exact\n', name, sol.stats.nfailed);
    fprintf('CHK|%s_vdp_nfevals|%d|%s\n', name, sol.stats.nfevals, evrule);
    fprintf('CHK|%s_vdp_npds|%d|exact\n', name, sol.stats.npds);
    fprintf('CHK|%s_vdp_ndecomps|%d|exact\n', name, sol.stats.ndecomps);
    fprintf('CHK|%s_vdp_nsolves|%d|exact\n', name, sol.stats.nsolves);
    fprintf('CHK|%s_vdp_final|%.17g|%s\n', name, sol.y(1, end), valrule);
    fprintf('CHK|%s_vdp_solver|%s|exact\n', name, sol.solver);
    z = deval(sol, [500 1500 2500]);
    fprintf('CHK|%s_vdp_deval_shape|%s|shape\n', name, mat2str(size(z)));
    fprintf('CHK|%s_vdp_deval|%.17g|%s\n', name, z(1, 2), valrule);
end

% --- van der Pol again with the Jacobian in closed form: every count exact on all four ----------
vdpjac = @(t, y) [0, 1; -2000 * y(1) * y(2) - 1, 1000 * (1 - y(1)^2)];
for i = 1:4
    name = stiff{i};
    f = str2func(name);
    sol = f(vdp1000, [0 3000], [2; 0], odeset('Jacobian', vdpjac));
    fprintf('CHK|%s_vdpj_nsteps|%d|exact\n', name, sol.stats.nsteps);
    fprintf('CHK|%s_vdpj_nfevals|%d|exact\n', name, sol.stats.nfevals);
    fprintf('CHK|%s_vdpj_npds|%d|exact\n', name, sol.stats.npds);
    fprintf('CHK|%s_vdpj_final|%.17g|rel=1e-6\n', name, sol.y(1, end));
end

% --- Robertson as an ODE (hb1ode): the classic stiff chemistry, to t = 4e6 ----------------------
hb1 = @(t, y) [-0.04 * y(1) + 1e4 * y(2) * y(3); ...
    0.04 * y(1) - 1e4 * y(2) * y(3) - 3e7 * y(2)^2; 3e7 * y(2)^2];
hb1jac = @(t, y) [-0.04, 1e4 * y(3), 1e4 * y(2); ...
    0.04, -1e4 * y(3) - 6e7 * y(2), -1e4 * y(2); 0, 6e7 * y(2), 0];
tight = odeset('RelTol', 1e-4, 'AbsTol', [1e-8 1e-14 1e-6]);
for i = 1:4
    name = stiff{i};
    f = str2func(name);
    sol = f(hb1, [0 4e6], [1; 0; 0], tight);
    % ode23s's Jacobian is differenced afresh at every step, so this is the run that parts.
    if strcmp(name, 'ode23s')
        rule = 'rel=2e-2';
    else
        rule = 'exact';
    end
    fprintf('CHK|%s_hb1_nsteps|%d|%s\n', name, sol.stats.nsteps, rule);
    fprintf('CHK|%s_hb1_nfevals|%d|%s\n', name, sol.stats.nfevals, rule);
    fprintf('CHK|%s_hb1_y3|%.17g|rel=1e-5\n', name, sol.y(3, end));

    sol = f(hb1, [0 4e6], [1; 0; 0], odeset(tight, 'Jacobian', hb1jac));
    fprintf('CHK|%s_hb1j_nsteps|%d|exact\n', name, sol.stats.nsteps);
    fprintf('CHK|%s_hb1j_nfailed|%d|exact\n', name, sol.stats.nfailed);
    fprintf('CHK|%s_hb1j_nfevals|%d|exact\n', name, sol.stats.nfevals);
    fprintf('CHK|%s_hb1j_npds|%d|exact\n', name, sol.stats.npds);
    fprintf('CHK|%s_hb1j_ndecomps|%d|exact\n', name, sol.stats.ndecomps);
    fprintf('CHK|%s_hb1j_y3|%.17g|rel=1e-8\n', name, sol.y(3, end));
end

% --- the Brusselator, N = 20: the sparsity pattern is the whole point ---------------------------
N = 20;
y0 = zeros(2 * N, 1);
for i = 1:N
    y0(2 * i - 1) = 1 + sin((2 * pi / (N + 1)) * i);
    y0(2 * i) = 3;
end
S = zeros(2 * N, 2 * N);
for i = 1:2 * N
    for j = max(1, i - 2):min(2 * N, i + 2)
        S(i, j) = 1;
    end
end
for i = 1:2:2 * N
    if i - 1 >= 1
        S(i, i - 1) = 0;
    end
end
for i = 2:2:2 * N
    if i + 1 <= 2 * N
        S(i, i + 1) = 0;
    end
end
solfull = ode15s(@bruss, [0 10], y0);
solpatt = ode15s(@bruss, [0 10], y0, odeset('JPattern', S));
fprintf('CHK|bruss_full_nsteps|%d|exact\n', solfull.stats.nsteps);
fprintf('CHK|bruss_full_nfevals|%d|exact\n', solfull.stats.nfevals);
fprintf('CHK|bruss_full_npds|%d|exact\n', solfull.stats.npds);
% Same steps, same Jacobians, fewer evaluations: the pattern groups the columns that share no row.
fprintf('CHK|bruss_patt_nsteps|%d|exact\n', solpatt.stats.nsteps);
fprintf('CHK|bruss_patt_nfevals|%d|exact\n', solpatt.stats.nfevals);
fprintf('CHK|bruss_patt_npds|%d|exact\n', solpatt.stats.npds);
fprintf('CHK|bruss_final|%.17g|rel=1e-6\n', solpatt.y(1, end));
fprintf('CHK|bruss_dif3d_shape|%s|shape\n', mat2str(size(solpatt.idata.dif3d)));
fprintf('CHK|bruss_kvec_end|%d|exact\n', solpatt.idata.kvec(end));

% --- hb1dae: ode15s and ode23t on an index-1 DAE with a singular diagonal mass matrix -----------
M = [1 0 0; 0 1 0; 0 0 0];
dae = odeset('Mass', M, 'RelTol', 1e-4, 'AbsTol', [1e-6 1e-10 1e-6]);
for name = {'ode15s', 'ode23t'}
    f = str2func(name{1});
    sol = f(@hb1dae, [0 4e6], [1; 0; 1e-3], dae);
    fprintf('CHK|%s_dae_nsteps|%d|exact\n', name{1}, sol.stats.nsteps);
    fprintf('CHK|%s_dae_nfailed|%d|exact\n', name{1}, sol.stats.nfailed);
    fprintf('CHK|%s_dae_nfevals|%d|exact\n', name{1}, sol.stats.nfevals);
    fprintf('CHK|%s_dae_npds|%d|exact\n', name{1}, sol.stats.npds);
    fprintf('CHK|%s_dae_ndecomps|%d|exact\n', name{1}, sol.stats.ndecomps);
    % The state the run started from is the one it worked out, not the guess it was handed.
    fprintf('CHK|%s_dae_start3|%.17g|rel=1e-6\n', name{1}, sol.y(3, 1));
    fprintf('CHK|%s_dae_final|%.17g|rel=1e-5\n', name{1}, sol.y(3, end));
    fprintf('CHK|%s_dae_sum|%.17g|rel=1e-6\n', name{1}, sum(sol.y(:, end)));
end

% --- amp1dae: a constant singular mass matrix that is not diagonal, so the decomposition works --
Mamp = zeros(5, 5);
c = 1e-6 * (1:3);
Mamp(1, 1) = -c(1); Mamp(1, 2) = c(1); Mamp(2, 1) = c(1); Mamp(2, 2) = -c(1);
Mamp(3, 3) = -c(2); Mamp(4, 4) = -c(3); Mamp(4, 5) = c(3); Mamp(5, 4) = c(3); Mamp(5, 5) = -c(3);
u0 = zeros(5, 1);
u0(2) = 3; u0(3) = 3; u0(4) = 6.1; u0(5) = 0.1;
sol = ode23t(@amp, [0 0.05], u0, odeset('Mass', Mamp));
fprintf('CHK|amp_nsteps|%d|exact\n', sol.stats.nsteps);
fprintf('CHK|amp_nfailed|%d|exact\n', sol.stats.nfailed);
fprintf('CHK|amp_nfevals|%d|exact\n', sol.stats.nfevals);
fprintf('CHK|amp_npds|%d|exact\n', sol.stats.npds);
fprintf('CHK|amp_nsolves|%d|exact\n', sol.stats.nsolves);
fprintf('CHK|amp_final|%.17g|rel=1e-6\n', sol.y(5, end));

% --- ihb1dae: decic finds the consistent pair, ode15i integrates it -----------------------------
oi = odeset('RelTol', 1e-4, 'AbsTol', [1e-6 1e-10 1e-6], 'Jacobian', {[], M});
[iy0, iyp0, resnrm] = decic(@ihb1, 0, [1; 0; 1e-3], [1 1 0], [0; 0; 0], [], oi);
fprintf('CHK|decic_y3|%.17g|abs=1e-12\n', iy0(3));
fprintf('CHK|decic_y1|%.17g|rel=1e-12\n', iy0(1));
fprintf('CHK|decic_yp1|%.17g|rel=1e-12\n', iyp0(1));
fprintf('CHK|decic_yp2|%.17g|rel=1e-12\n', iyp0(2));
fprintf('CHK|decic_yp3|%.17g|abs=1e-12\n', iyp0(3));
fprintf('CHK|decic_resnrm|%.17g|abs=1e-12\n', resnrm);
soli = ode15i(@ihb1, [0 4e6], iy0, iyp0, oi);
% ode15i tracks R2025b step for step for the first two hundred and forty steps and then parts on
% the same rounding the differenced Jacobian carries; the count is pinned within two per cent.
fprintf('CHK|i15_nsteps|%d|rel=2e-2\n', soli.stats.nsteps);
fprintf('CHK|i15_nfevals|%d|rel=2e-2\n', soli.stats.nfevals);
fprintf('CHK|i15_npds|%d|exact\n', soli.stats.npds);
fprintf('CHK|i15_solver|%s|exact\n', soli.solver);
fprintf('CHK|i15_final|%.17g|rel=1e-5\n', soli.y(3, end));
fprintf('CHK|i15_sum|%.17g|rel=1e-6\n', sum(soli.y(:, end)));
d = deval(soli, [1 100 1e4]);
fprintf('CHK|i15_deval_shape|%s|shape\n', mat2str(size(d)));
fprintf('CHK|i15_deval|%.17g|rel=1e-4\n', d(2, 2));
% kvec is one row and one entry per mesh point, whatever the step count came out at.
fprintf('CHK|i15_kvec_rows|%d|exact\n', size(soli.idata.kvec, 1));
fprintf('CHK|i15_kvec_mesh|%d|exact\n', numel(soli.idata.kvec) - numel(soli.x));
[it, iy] = ode15i(@ihb1, [0 1e4], iy0, iyp0, oi);
fprintf('CHK|i15_pair_cols|%d|exact\n', size(iy, 2));
fprintf('CHK|i15_pair_final|%.17g|rel=1e-4\n', iy(end, 1));

% --- the first two hundred steps of ode15i, which agree exactly ---------------------------------
short = ode15i(@ihb1, [0 100], iy0, iyp0, oi);
fprintf('CHK|i15_short_nsteps|%d|exact\n', short.stats.nsteps);
fprintf('CHK|i15_short_nfevals|%d|exact\n', short.stats.nfevals);
fprintf('CHK|i15_short_ndecomps|%d|exact\n', short.stats.ndecomps);
fprintf('CHK|i15_short_final|%.17g|rel=1e-9\n', short.y(2, end));

% --- events, Refine, a named grid, odextend and deval on every stiff solver ---------------------
decay = @(t, y) -100 * y + sin(t);
for i = 1:4
    name = stiff{i};
    f = str2func(name);
    [t, y, te, ye, ie] = f(decay, [0 5], 1, odeset('Events', @crossing));
    fprintf('CHK|%s_ev_te|%.17g|abs=1e-8\n', name, te(1));
    fprintf('CHK|%s_ev_stop|%.17g|abs=1e-8\n', name, t(end));
    fprintf('CHK|%s_ev_ie|%d|exact\n', name, ie(1));
    fprintf('CHK|%s_ev_ye|%.17g|rel=1e-7\n', name, ye(1));
    [t2, ~] = f(decay, [0 1], 1, odeset('Refine', 4));
    fprintf('CHK|%s_refine|%d|exact\n', name, numel(t2));
    [t3, y3] = f(decay, 0:0.1:1, 1);
    fprintf('CHK|%s_grid_rows|%d|exact\n', name, numel(t3));
    fprintf('CHK|%s_grid_y|%.17g|rel=1e-7\n', name, y3(6));
    s = f(decay, [0 1], 1);
    e = odextend(s, decay, 2);
    fprintf('CHK|%s_ext_mesh|%d|exact\n', name, numel(e.x));
    fprintf('CHK|%s_ext_nsteps|%d|exact\n', name, e.stats.nsteps);
    fprintf('CHK|%s_ext_npds|%d|exact\n', name, e.stats.npds);
    fprintf('CHK|%s_ext_nsolves|%d|exact\n', name, e.stats.nsolves);
    fprintf('CHK|%s_ext_final|%.17g|rel=1e-7\n', name, e.y(end));
    q = deval(e, [0.5 1.5]);
    fprintf('CHK|%s_ext_deval|%.17g|rel=1e-7\n', name, q(2));
    [~, qp] = deval(e, 1.5);
    fprintf('CHK|%s_ext_devalp|%.17g|rel=1e-6\n', name, qp);
end

% --- odextend on a fully implicit solution, which continues from the slope it ended at ----------
ilin = @(t, y, yp) yp + 3 * y - 1;
si = ode15i(ilin, [0 1], 1, -2, odeset('RelTol', 1e-8, 'AbsTol', 1e-10));
% The slope a run ends at is a predictor's, one order below the state it belongs to.
fprintf('CHK|i15_ext_ypfinal|%.17g|rel=1e-5\n', si.extdata.ypfinal);
ei = odextend(si, ilin, 2);
fprintf('CHK|i15_ext_mesh|%d|exact\n', numel(ei.x));
fprintf('CHK|i15_ext_final|%.17g|rel=1e-7\n', ei.y(end));
fprintf('CHK|i15_ext_deval|%.17g|rel=1e-7\n', deval(ei, 1.5));

% --- the options only these solvers read -------------------------------------------------------
solb = ode15s(@bruss, [0 10], y0, odeset('BDF', 'on'));
fprintf('CHK|bdf_nsteps|%d|exact\n', solb.stats.nsteps);
fprintf('CHK|bdf_nfevals|%d|exact\n', solb.stats.nfevals);
fprintf('CHK|bdf_final|%.17g|rel=1e-6\n', solb.y(1, end));
solm = ode15s(@bruss, [0 10], y0, odeset('MaxOrder', 2));
fprintf('CHK|maxorder_nsteps|%d|exact\n', solm.stats.nsteps);
fprintf('CHK|maxorder_kmax|%d|exact\n', max(solm.idata.kvec));
fprintf('CHK|maxorder_width|%d|exact\n', size(solm.idata.dif3d, 2));
solc = ode15s(vdp1000, [0 100], [2; 0], odeset('JConstant', 'on'));
fprintf('CHK|jconstant_npds|%d|exact\n', solc.stats.npds);
soln = ode15s(@bruss, [0 10], y0, odeset('NormControl', 'on'));
fprintf('CHK|normcontrol_nsteps|%d|exact\n', soln.stats.nsteps);
fprintf('CHK|normcontrol_nfevals|%d|exact\n', soln.stats.nfevals);
fprintf('CHK|normcontrol_final|%.17g|rel=1e-6\n', soln.y(1, end));
solnt = ode23t(vdp1000, [0 3000], [2; 0], odeset('NormControl', 'on'));
fprintf('CHK|normcontrol_23t_nsteps|%d|exact\n', solnt.stats.nsteps);
% A supplied consistent slope is taken as given, so the search for one never runs.
sols = ode15s(@hb1dae, [0 4e6], [1; 0; 0], odeset(dae, 'InitialSlope', [-0.04; 0.04; 0]));
fprintf('CHK|initialslope_nsteps|%d|exact\n', sols.stats.nsteps);
fprintf('CHK|initialslope_start3|%.17g|abs=1e-12\n', sols.y(3, 1));
neg = @(t, y) -abs(y);
for name = {'ode15s', 'ode23t', 'ode23tb'}
    f = str2func(name{1});
    s = f(neg, [0 40], 1, odeset('NonNegative', 1));
    fprintf('CHK|%s_nn_nsteps|%d|exact\n', name{1}, s.stats.nsteps);
    fprintf('CHK|%s_nn_min|%.17g|abs=1e-9\n', name{1}, min(s.y));
    fprintf('CHK|%s_nn_final|%.17g|abs=1e-9\n', name{1}, s.y(end));
end

% --- what each solver refuses, and what it only warns about ------------------------------------
refused = 0;
try
    ode23s(vdp1000, [0 1], [2; 0], odeset('Mass', @(t) [1 0; 0 1], 'MStateDependence', 'none'));
catch
    refused = refused + 1;
end
try
    ode23s(vdp1000, [0 1], [2; 0], odeset('Mass', eye(2), 'MassSingular', 'yes'));
catch
    refused = refused + 1;
end
try
    ode23tb(vdp1000, [0 1], [2; 0], odeset('Mass', eye(2), 'MassSingular', 'yes'));
catch
    refused = refused + 1;
end
try
    ode15s(vdp1000, [0 1], [2; 0], odeset('Mass', zeros(2)));
catch
    refused = refused + 1;
end
fprintf('CHK|refusals|%d|exact\n', refused);
% A non-singular mass matrix declared singular is still an ODE to ode15s, which says so and solves.
solms = ode15s(vdp1000, [0 100], [2; 0], odeset('Mass', [2 1; 1 3], 'MassSingular', 'no'));
fprintf('CHK|massno_nsteps|%d|exact\n', solms.stats.nsteps);
fprintf('CHK|massno_final|%.17g|rel=1e-6\n', solms.y(1, end));
% ode15s and ode23t decline a non-negativity constraint beside a mass matrix rather than refusing.
solmn = ode15s(vdp1000, [0 100], [2; 0], odeset('Mass', [2 1; 1 3], 'NonNegative', 1));
fprintf('CHK|massnn_nsteps|%d|exact\n', solmn.stats.nsteps);
fprintf('CHK|massnn_min|%.17g|rel=1e-6\n', min(solmn.y(1, :)));

% --- Vectorized changes the calls and not the answer -------------------------------------------
solv = ode15s(@brussv, [0 10], y0, odeset('JPattern', S, 'Vectorized', 'on'));
fprintf('CHK|vector_nsteps|%d|exact\n', solv.stats.nsteps);
fprintf('CHK|vector_nfevals|%d|exact\n', solv.stats.nfevals);
fprintf('CHK|vector_final|%.17g|rel=1e-6\n', solv.y(1, end));

% --- the six statistics an implicit solver prints, which the explicit family has only three of --
ode15s(vdp1000, [0 100], [2; 0], odeset('Stats', 'on'));

function dydt = bruss(t, y) %#ok<INUSD>
N = 20;
c = 0.02 * (N + 1)^2;
dydt = zeros(2 * N, 1);
i = 1;
dydt(i) = 1 + y(i + 1) * y(i)^2 - 4 * y(i) + c * (1 - 2 * y(i) + y(i + 2));
dydt(i + 1) = 3 * y(i) - y(i + 1) * y(i)^2 + c * (3 - 2 * y(i + 1) + y(i + 3));
for i = 3:2:2 * N - 3
    dydt(i) = 1 + y(i + 1) * y(i)^2 - 4 * y(i) + c * (y(i - 2) - 2 * y(i) + y(i + 2));
    dydt(i + 1) = 3 * y(i) - y(i + 1) * y(i)^2 + c * (y(i - 1) - 2 * y(i + 1) + y(i + 3));
end
i = 2 * N - 1;
dydt(i) = 1 + y(i + 1) * y(i)^2 - 4 * y(i) + c * (y(i - 2) - 2 * y(i) + 1);
dydt(i + 1) = 3 * y(i) - y(i + 1) * y(i)^2 + c * (y(i - 1) - 2 * y(i + 1) + 3);
end

function dydt = brussv(t, y) %#ok<INUSD>
N = 20;
c = 0.02 * (N + 1)^2;
dydt = zeros(2 * N, size(y, 2));
i = 1;
dydt(i, :) = 1 + y(i + 1, :) .* y(i, :).^2 - 4 * y(i, :) + c * (1 - 2 * y(i, :) + y(i + 2, :));
dydt(i + 1, :) = 3 * y(i, :) - y(i + 1, :) .* y(i, :).^2 + c * (3 - 2 * y(i + 1, :) + y(i + 3, :));
i = 3:2:2 * N - 3;
dydt(i, :) = 1 + y(i + 1, :) .* y(i, :).^2 - 4 * y(i, :) + c * (y(i - 2, :) - 2 * y(i, :) + y(i + 2, :));
dydt(i + 1, :) = 3 * y(i, :) - y(i + 1, :) .* y(i, :).^2 + c * (y(i - 1, :) - 2 * y(i + 1, :) + y(i + 3, :));
i = 2 * N - 1;
dydt(i, :) = 1 + y(i + 1, :) .* y(i, :).^2 - 4 * y(i, :) + c * (y(i - 2, :) - 2 * y(i, :) + 1);
dydt(i + 1, :) = 3 * y(i, :) - y(i + 1, :) .* y(i, :).^2 + c * (y(i - 1, :) - 2 * y(i + 1, :) + 3);
end

function out = hb1dae(t, y) %#ok<INUSD>
out = [-0.04 * y(1) + 1e4 * y(2) * y(3); ...
    0.04 * y(1) - 1e4 * y(2) * y(3) - 3e7 * y(2)^2; ...
    y(1) + y(2) + y(3) - 1];
end

function res = ihb1(t, y, yp) %#ok<INUSD>
res = [yp(1) + 0.04 * y(1) - 1e4 * y(2) * y(3); ...
    yp(2) - 0.04 * y(1) + 1e4 * y(2) * y(3) + 3e7 * y(2)^2; ...
    y(1) + y(2) + y(3) - 1];
end

function dudt = amp(t, u)
Ub = 6; R0 = 1000; R = 9000; alpha = 0.99; beta = 1e-6; Uf = 0.026;
Ue = 0.4 * sin(200 * pi * t);
f23 = beta * (exp((u(2) - u(3)) / Uf) - 1);
dudt = [-(Ue - u(1)) / R0; ...
    -(Ub / R - u(2) * 2 / R - (1 - alpha) * f23); ...
    -(f23 - u(3) / R); ...
    -((Ub - u(4)) / R - alpha * f23); ...
    u(5) / R];
end

function [value, isterminal, direction] = crossing(t, y) %#ok<INUSL>
value = y - 0.02;
isterminal = 1;
direction = -1;
end
