% m124_ode45.m -- the explicit solver that exists today, pinned by what it does and what it answers.
% The step counts are exact: they are the honest test that the algorithm matches, not only the
% answer. Every form here is accepted by both engines (M119/M121 closed Refine, MaxStep and the
% solution structure); the forms ode45 still refuses are capability-probe rows, not fixture lines.

% A decay, default options: nsteps is the algorithm; the final value is the answer.
sol = ode45(@(t, y) -2 * y, [0 1], 1);
fprintf('CHK|decay_nsteps|%d|exact\n', sol.stats.nsteps);
fprintf('CHK|decay_nfailed|%d|exact\n', sol.stats.nfailed);
fprintf('CHK|decay_nfevals|%d|exact\n', sol.stats.nfevals);
fprintf('CHK|decay_final|%.17g|rel=1e-10\n', sol.y(end));
fprintf('CHK|decay_solver|%s|exact\n', sol.solver);

% The harmonic oscillator at Refine 1 and at the default Refine 4: the count of rows is the rule.
osc = @(t, y) [y(2); -y(1)];
[t1, y1] = ode45(osc, [0 10], [1; 0], odeset('Refine', 1));
fprintf('CHK|osc_refine1_rows|%d|exact\n', numel(t1));
fprintf('CHK|osc_refine1_shape|%s|shape\n', mat2str(size(y1)));
fprintf('CHK|osc_refine1_final|%.17g|rel=1e-6\n', y1(end, 1));
[t4, y4] = ode45(osc, [0 10], [1; 0]);
fprintf('CHK|osc_refine4_rows|%d|exact\n', numel(t4));
fprintf('CHK|osc_refine4_t2|%.17g|rel=1e-12\n', t4(2));

% MaxStep caps the step; every row after the first is the cap, so the count is arithmetic.
[tm, ym] = ode45(osc, [0 10], [1; 0], odeset('MaxStep', 0.05));
fprintf('CHK|osc_maxstep_rows|%d|exact\n', numel(tm));
fprintf('CHK|osc_maxstep_final|%.17g|rel=1e-8\n', ym(end, 1));

% Tightened tolerances: the solver promises eight figures, and is asked for exactly that.
[tt, yt] = ode45(osc, [0 10], [1; 0], odeset('RelTol', 1e-8, 'AbsTol', 1e-10));
fprintf('CHK|osc_tight_rows|%d|exact\n', numel(tt));
fprintf('CHK|osc_tight_final|%.17g|rel=1e-7\n', yt(end, 1));
fprintf('CHK|osc_tight_err|%d|exact\n', double(abs(yt(end, 1) - cos(10)) < 1e-7));

% A requested output grid: the rows are the grid, whatever the steps were.
[tg, yg] = ode45(osc, 0:0.5:10, [1; 0]);
fprintf('CHK|osc_grid_rows|%d|exact\n', numel(tg));
fprintf('CHK|osc_grid_last_t|%.17g|exact\n', tg(end));
fprintf('CHK|osc_grid_y5|%.17g|rel=1e-5\n', yg(5, 1));

% deval reads the solution's own interpolant, so it is pinned to the interpolant's accuracy.
sol2 = ode45(osc, [0 10], [1; 0]);
z = deval(sol2, [1 2.5 7]);
fprintf('CHK|deval_shape|%s|shape\n', mat2str(size(z)));
fprintf('CHK|deval_1|%.17g|rel=1e-5\n', z(1, 1));
fprintf('CHK|deval_2|%.17g|rel=1e-5\n', z(1, 2));
fprintf('CHK|deval_3|%.17g|rel=1e-5\n', z(2, 3));

% Lorenz to t = 20 with defaults: chaos amplifies any difference in the arithmetic, so only the
% step count is exact and the endpoint is pinned loosely.
lorenz = @(t, y) [10 * (y(2) - y(1)); y(1) * (28 - y(3)) - y(2); y(1) * y(2) - (8 / 3) * y(3)];
[tl, yl] = ode45(lorenz, [0 20], [1; 1; 1]);
fprintf('CHK|lorenz_rows|%d|exact\n', numel(tl));
fprintf('CHK|lorenz_zmax|%.17g|rel=1e-3\n', max(yl(:, 3)));

% odeset/odeget round trip, and the default a missing field answers.
o = odeset('RelTol', 1e-5, 'AbsTol', 1e-7, 'Refine', 2);
fprintf('CHK|odeget_reltol|%.17g|exact\n', odeget(o, 'RelTol'));
fprintf('CHK|odeget_refine|%.17g|exact\n', odeget(o, 'Refine'));
fprintf('CHK|odeget_default|%.17g|exact\n', odeget(o, 'MaxStep', 0.25));
fprintf('CHK|odeget_empty|%d|exact\n', isempty(odeget(o, 'MaxStep')));
