% m125_ode_explicit.m -- the explicit family (ode23, ode45, ode78, ode89, ode113) and the options
% ode45 used to ignore, pinned by what each solver does and what it answers. nsteps, nfailed and
% nfevals are exact on every problem: they are the algorithm, and a step-growth cap or an error
% weight that differs from MATLAB's by one constant shows as a count off by one, which no
% tolerance hides. Endpoints are pinned at the accuracy the run was asked for; event times at the
% accuracy the bracketing search promises; deval at the interpolant's.
solvers = {'ode23', 'ode45', 'ode78', 'ode89', 'ode113'};
% The Verner pairs steer each step from the last bits of an error estimate that has cancelled
% eight orders of magnitude, so two implementations of one tableau agree on every step count
% and part at 1e-8 in the state; the older pairs and the Adams method agree to 1e-9 and better.
rules = {'rel=1e-9', 'rel=1e-9', 'rel=1e-6', 'rel=1e-6', 'rel=1e-9'};

% --- van der Pol, mu = 1: default tolerances, the solution structure, the pair, and deval -------
vdp = @(t, y) [y(2); (1 - y(1)^2) * y(2) - y(1)];
for i = 1:5
    name = solvers{i};
    f = str2func(name);
    sol = f(vdp, [0 20], [2; 0]);
    fprintf('CHK|%s_vdp_nsteps|%d|exact\n', name, sol.stats.nsteps);
    fprintf('CHK|%s_vdp_nfailed|%d|exact\n', name, sol.stats.nfailed);
    fprintf('CHK|%s_vdp_nfevals|%d|exact\n', name, sol.stats.nfevals);
    fprintf('CHK|%s_vdp_final|%.17g|%s\n', name, sol.y(1, end), rules{i});
    fprintf('CHK|%s_vdp_solver|%s|exact\n', name, sol.solver);
    [t, y] = f(vdp, [0 20], [2; 0]);
    fprintf('CHK|%s_vdp_rows|%d|exact\n', name, numel(t));
    fprintf('CHK|%s_vdp_pair_final|%.17g|%s\n', name, y(end, 2), rules{i});
    z = deval(sol, [1.5 4 7.25 11 16.5]);
    fprintf('CHK|%s_vdp_deval_shape|%s|shape\n', name, mat2str(size(z)));
    fprintf('CHK|%s_vdp_deval_2|%.17g|%s\n', name, z(1, 2), rules{i});
    fprintf('CHK|%s_vdp_deval_5|%.17g|%s\n', name, z(2, 5), rules{i});
end

% --- Euler's equations of a rigid body (rigidode): tightened tolerances and a requested grid --
rigid = @(t, y) [y(2) * y(3); -y(1) * y(3); -0.51 * y(1) * y(2)];
tight = odeset('RelTol', 1e-6, 'AbsTol', 1e-8);
for i = 1:5
    name = solvers{i};
    f = str2func(name);
    sol = f(rigid, [0 12], [0; 1; 1], tight);
    fprintf('CHK|%s_rigid_nsteps|%d|exact\n', name, sol.stats.nsteps);
    fprintf('CHK|%s_rigid_nfailed|%d|exact\n', name, sol.stats.nfailed);
    fprintf('CHK|%s_rigid_nfevals|%d|exact\n', name, sol.stats.nfevals);
    fprintf('CHK|%s_rigid_final|%.17g|%s\n', name, sol.y(3, end), rules{i});
    [tg, yg] = f(rigid, 0:0.5:12, [0; 1; 1], tight);
    fprintf('CHK|%s_rigid_grid_rows|%d|exact\n', name, numel(tg));
    fprintf('CHK|%s_rigid_grid_y|%.17g|%s\n', name, yg(7, 1), rules{i});
end

% --- the restricted three-body orbit (orbitode): a terminal event and a non-terminal one ------
y0 = [1.2; 0; 0; -1.04935750983031990726];
orbit = @(t, y) orbitode(t, y);
events = @(t, y) orbitevents(t, y, y0);
loose = odeset('RelTol', 1e-5, 'AbsTol', 1e-4, 'Events', events);
for i = 1:5
    name = solvers{i};
    f = str2func(name);
    [t, y, te, ye, ie] = f(orbit, [0 6.19216933131963970674], y0, loose);
    % Distinct events: the bracketing search can report a root it has just passed a second time
    % when rounding leaves the value on the wrong side of zero, and which engine does so on a
    % given step is decided by the last bit of the trajectory, not by the algorithm.
    fprintf('CHK|%s_orbit_ndistinct|%d|exact\n', name, numel(unique(round(te * 1e6))));
    fprintf('CHK|%s_orbit_ie|%s|exact\n', name, mat2str(unique(ie)'));
    fprintf('CHK|%s_orbit_te_last|%.17g|abs=1e-8\n', name, te(end));
    fprintf('CHK|%s_orbit_te_first|%.17g|abs=1e-8\n', name, te(1));
    fprintf('CHK|%s_orbit_stop|%.17g|abs=1e-8\n', name, t(end));
    fprintf('CHK|%s_orbit_ye|%.17g|rel=1e-6\n', name, ye(end, 1));
    fprintf('CHK|%s_orbit_rows|%d|exact\n', name, numel(t));
end
sol = ode45(orbit, [0 6.19216933131963970674], y0, loose);
fprintf('CHK|ode45_orbit_sol_xe|%.17g|abs=1e-8\n', sol.xe(end));
fprintf('CHK|ode45_orbit_sol_ie|%s|exact\n', mat2str(sol.ie));
fprintf('CHK|ode45_orbit_sol_nsteps|%d|exact\n', sol.stats.nsteps);
fprintf('CHK|ode45_orbit_sol_tfinal|%.17g|abs=1e-8\n', sol.stats.tfinal);

% --- the bouncing ball (ballode): a terminal event, then odextend from the bounce ------------
ball = @(t, y) [y(2); -9.8];
bounce = @(t, y) ballevents(t, y);
sol = ode23(ball, [0 30], [0; 20], odeset('Events', bounce, 'Refine', 1));
fprintf('CHK|ball_xe|%.17g|abs=1e-9\n', sol.xe);
fprintf('CHK|ball_ye2|%.17g|rel=1e-9\n', sol.ye(2));
fprintf('CHK|ball_nsteps|%d|exact\n', sol.stats.nsteps);
fprintf('CHK|ball_mesh|%d|exact\n', numel(sol.x));
ext = odextend(sol, ball, 30, [0; -0.9 * sol.ye(2)]);
fprintf('CHK|ball_ext_mesh|%d|exact\n', numel(ext.x));
fprintf('CHK|ball_ext_xe1|%.17g|abs=1e-9\n', ext.xe(1));
fprintf('CHK|ball_ext_xe2|%.17g|abs=1e-9\n', ext.xe(2));
fprintf('CHK|ball_ext_nsteps|%d|exact\n', ext.stats.nsteps);
fprintf('CHK|ball_ext_nfevals|%d|exact\n', ext.stats.nfevals);
fprintf('CHK|ball_ext_solver|%s|exact\n', ext.solver);
d = deval(ext, [1 3 5]);
fprintf('CHK|ball_ext_deval|%.17g|rel=1e-9\n', d(1, 3));
ext2 = odextend(ext, [], 40);
fprintf('CHK|ball_ext2_tfinal|%.17g|abs=1e-9\n', ext2.stats.tfinal);
fprintf('CHK|ball_ext2_nevents|%d|exact\n', numel(ext2.xe));

% --- Lorenz to t = 20: the step count is exact, the endpoint is chaos and pinned loosely -----
lorenz = @(t, y) [10 * (y(2) - y(1)); y(1) * (28 - y(3)) - y(2); y(1) * y(2) - (8 / 3) * y(3)];
for i = 1:5
    name = solvers{i};
    f = str2func(name);
    [t, y] = f(lorenz, [0 20], [1; 1; 1]);
    fprintf('CHK|%s_lorenz_rows|%d|exact\n', name, numel(t));
    fprintf('CHK|%s_lorenz_zmax|%.17g|rel=1e-3\n', name, max(y(:, 3)));
end

% --- a constant mass matrix, and one that depends on time --------------------------------------
osc = @(t, y) [y(2); -y(1)];
M = [2 1; 1 3];
for i = 1:5
    name = solvers{i};
    f = str2func(name);
    [t, y] = f(osc, [0 5], [1; 0], odeset('Mass', M));
    fprintf('CHK|%s_mass_rows|%d|exact\n', name, numel(t));
    fprintf('CHK|%s_mass_final|%.17g|%s\n', name, y(end, 1), rules{i});
end
[t, y] = ode23(osc, [0 5], [1; 0], odeset('Mass', @(t) [2 + t, 0; 0, 1], 'MStateDependence', 'none'));
fprintf('CHK|ode23_masst_rows|%d|exact\n', numel(t));
fprintf('CHK|ode23_masst_final|%.17g|rel=1e-9\n', y(end, 1));

% --- NonNegative on a decay that would cross zero: the count carries the extra evaluations ----
decay = @(t, y) -abs(y);
for i = 1:5
    name = solvers{i};
    f = str2func(name);
    sol = f(decay, [0 40], 1, odeset('NonNegative', 1));
    fprintf('CHK|%s_nonneg_nsteps|%d|exact\n', name, sol.stats.nsteps);
    fprintf('CHK|%s_nonneg_nfevals|%d|exact\n', name, sol.stats.nfevals);
    fprintf('CHK|%s_nonneg_min|%.17g|abs=1e-9\n', name, min(sol.y));
    fprintf('CHK|%s_nonneg_final|%.17g|abs=1e-9\n', name, sol.y(end));
end

% --- NormControl, Refine, OutputSel, and the shapes of what a solution carries ---------------
for i = 1:5
    name = solvers{i};
    f = str2func(name);
    sol = f(vdp, [0 20], [2; 0], odeset('NormControl', 'on'));
    fprintf('CHK|%s_normcontrol_nsteps|%d|exact\n', name, sol.stats.nsteps);
    [t, y] = f(vdp, [0 20], [2; 0], odeset('Refine', 3));
    fprintf('CHK|%s_refine3_rows|%d|exact\n', name, numel(t));
end
sol = ode45(osc, [0 4], [1; 0]);
fprintf('CHK|ode45_f3d_shape|%s|shape\n', mat2str(size(sol.idata.f3d)));
sol = ode23(osc, [0 4], [1; 0]);
fprintf('CHK|ode23_f3d_shape|%s|shape\n', mat2str(size(sol.idata.f3d)));
sol = ode78(osc, [0 4], [1; 0]);
fprintf('CHK|ode78_f3d_shape|%s|shape\n', mat2str(size(sol.idata.f3d)));
sol = ode89(osc, [0 4], [1; 0]);
fprintf('CHK|ode89_f3d_shape|%s|shape\n', mat2str(size(sol.idata.f3d)));
sol = ode113(osc, [0 4], [1; 0]);
fprintf('CHK|ode113_phi3d_shape|%s|shape\n', mat2str(size(sol.idata.phi3d)));
fprintf('CHK|ode113_psi2d_shape|%s|shape\n', mat2str(size(sol.idata.psi2d)));
fprintf('CHK|ode113_klast_end|%d|exact\n', sol.idata.klastvec(end));
[z, zp] = deval(sol, [1 2 3]);
fprintf('CHK|ode113_deval_yp|%.17g|rel=1e-8\n', zp(2, 2));
[z, zp] = deval(ode89(osc, [0 4], [1; 0]), [1 2 3]);
fprintf('CHK|ode89_deval_yp|%.17g|rel=1e-8\n', zp(1, 3));
o = odeset('JPattern', [1 0; 0 1], 'BDF', 'on', 'MaxOrder', 3, 'Vectorized', 'on', 'MStateDependence', 'strong');
fprintf('CHK|odeset_stored|%d|exact\n', numel(odeget(o, 'JPattern')) + double(strcmp(odeget(o, 'BDF'), 'on')) + odeget(o, 'MaxOrder'));
[t, y] = ode45(vdp, [0 20], [2; 0], odeset('OutputSel', 2, 'Refine', 1));
fprintf('CHK|ode45_outputsel_rows|%d|exact\n', numel(t));

% --- the R2023b solver object is declined by name ---------------------------------------------
has_ode_object = 1;
try
    F = ode; %#ok<NASGU>
catch
    has_ode_object = 0;
end
fprintf('CHK|ode_object|%d|div=ADR0127\n', has_ode_object);

function dydt = orbitode(t, y) %#ok<INUSD>
mu = 1 / 82.45;
mustar = 1 - mu;
r13 = ((y(1) + mu)^2 + y(2)^2)^1.5;
r23 = ((y(1) - mustar)^2 + y(2)^2)^1.5;
dydt = [y(3); y(4); ...
    2 * y(4) + y(1) - mustar * ((y(1) + mu) / r13) - mu * ((y(1) - mustar) / r23); ...
    -2 * y(3) + y(2) - mustar * (y(2) / r13) - mu * (y(2) / r23)];
end

function [value, isterminal, direction] = orbitevents(t, y, y0) %#ok<INUSL>
% The distance from the starting point: a minimum ends the run, a maximum is only noted.
dDSQdt = 2 * ((y(1:2) - y0(1:2))' * y(3:4));
value = [dDSQdt; dDSQdt];
isterminal = [1; 0];
direction = [1; -1];
end

function [value, isterminal, direction] = ballevents(t, y) %#ok<INUSL>
value = y(1);
isterminal = 1;
direction = -1;
end
