% m130_bvp_dde.m -- the boundary-value collocation pair (bvp4c, bvp5c) with bvpinit, bvpxtend,
% bvpset and bvpget, and the three delay solvers (dde23, ddesd, ddensd) with ddeset and ddeget,
% pinned on the problems the documentation ships: twobvp, mat4bvp, shockbvp, emdenbvp, fsbvp,
% threebvp, rcbvp and ddex1 through ddex5.
%
% The strong claim here is the mesh size. A collocation solver's answer is decided by a residual
% estimate, a redistribution rule and a Newton convergence test, and a single differing constant in
% any of them moves the count. Every mesh count below is pinned exact, and so is the cost -- the
% calls to the ODE function and to the boundary condition function -- because those count the
% Jacobian's own differencing and would drift if the midpoint rule or the increment differed.
%
% One run is pinned loosely and is named where it appears: ddex2, the baroreflex problem, has a
% right-hand side with two seventh powers in it, and R2025b's pow and .NET's disagree in the last
% bit often enough that over six hundred steps and a hundred and twenty rejected ones the two
% engines take different steps. Every other delay run here matches step for step.

% --- twobvp: two solutions of y'' = -|y| on [0, 4] ----------------------------------------------
for branch = [1 -1]
    solinit = bvpinit(linspace(0, 4, 5), [branch 0]);
    sol = bvp4c(@twoode, @twobc, solinit);
    tag = sprintf('twobvp%+d', branch);
    fprintf('CHK|%s_solver|%s|exact\n', tag, sol.solver);
    fprintf('CHK|%s_n|%d|exact\n', tag, sol.stats.nmeshpoints);
    fprintf('CHK|%s_numelx|%d|exact\n', tag, numel(sol.x));
    fprintf('CHK|%s_maxres|%.17g|rel=1e-6\n', tag, sol.stats.maxres);
    fprintf('CHK|%s_nodeevals|%d|exact\n', tag, sol.stats.nODEevals);
    fprintf('CHK|%s_nbcevals|%d|exact\n', tag, sol.stats.nBCevals);
    fprintf('CHK|%s_yshape|%s|shape\n', tag, mat2str(size(sol.y)));
    fprintf('CHK|%s_ypshape|%s|shape\n', tag, mat2str(size(sol.yp)));
    fprintf('CHK|%s_yp0|%.17g|rel=1e-6\n', tag, sol.y(2, 1));
    fprintf('CHK|%s_yend|%.17g|rel=1e-6\n', tag, sol.y(1, end));
    fprintf('CHK|%s_ypend|%.17g|rel=1e-6\n', tag, sol.yp(2, end));
    z = deval(sol, [0.5 1.5 2.5 3.5]);
    fprintf('CHK|%s_deval_shape|%s|shape\n', tag, mat2str(size(z)));
    fprintf('CHK|%s_deval1|%.17g|rel=1e-6\n', tag, z(1, 2));
    fprintf('CHK|%s_deval2|%.17g|rel=1e-6\n', tag, z(2, 3));

    sol5 = bvp5c(@twoode, @twobc, bvpinit(linspace(0, 4, 5), [branch 0]));
    fprintf('CHK|%s_5c_solver|%s|exact\n', tag, sol5.solver);
    fprintf('CHK|%s_5c_n|%d|exact\n', tag, sol5.stats.nmeshpoints);
    fprintf('CHK|%s_5c_maxerr|%.17g|rel=1e-6\n', tag, sol5.stats.maxerr);
    fprintf('CHK|%s_5c_nodeevals|%d|exact\n', tag, sol5.stats.nODEevals);
    fprintf('CHK|%s_5c_nbcevals|%d|exact\n', tag, sol5.stats.nBCevals);
    fprintf('CHK|%s_5c_ymidshape|%s|shape\n', tag, mat2str(size(sol5.idata.ymid)));
    fprintf('CHK|%s_5c_yp0|%.17g|rel=1e-6\n', tag, sol5.y(2, 1));
    z5 = deval(sol5, [0.5 1.5 2.5 3.5]);
    fprintf('CHK|%s_5c_deval1|%.17g|rel=1e-6\n', tag, z5(1, 2));
    fprintf('CHK|%s_5c_deval2|%.17g|rel=1e-6\n', tag, z5(2, 3));
end

% --- mat4bvp: the fourth eigenvalue of Mathieu's equation, an unknown parameter -----------------
solinit = bvpinit(linspace(0, pi, 10), @mat4init, 15);
sol = bvp4c(@mat4ode, @mat4bc, solinit);
fprintf('CHK|mat4_n|%d|exact\n', sol.stats.nmeshpoints);
fprintf('CHK|mat4_maxres|%.17g|rel=1e-6\n', sol.stats.maxres);
fprintf('CHK|mat4_nodeevals|%d|exact\n', sol.stats.nODEevals);
fprintf('CHK|mat4_nbcevals|%d|exact\n', sol.stats.nBCevals);
fprintf('CHK|mat4_lambda|%.17g|rel=1e-6\n', sol.parameters);
fprintf('CHK|mat4_pshape|%s|shape\n', mat2str(size(sol.parameters)));
w = deval(sol, pi / 2);
fprintf('CHK|mat4_ymid|%.17g|rel=1e-6\n', w(1));

sol5 = bvp5c(@mat4ode, @mat4bc, bvpinit(linspace(0, pi, 10), @mat4init, 15));
fprintf('CHK|mat4_5c_n|%d|exact\n', sol5.stats.nmeshpoints);
fprintf('CHK|mat4_5c_maxerr|%.17g|rel=1e-6\n', sol5.stats.maxerr);
fprintf('CHK|mat4_5c_nodeevals|%d|exact\n', sol5.stats.nODEevals);
fprintf('CHK|mat4_5c_lambda|%.17g|rel=1e-6\n', sol5.parameters);

% --- fsbvp: Falkner-Skan, solved again on ever longer intervals through bvpxtend ---------------
sol = bvp4c(@fsode, @fsbc, bvpinit(linspace(0, 3, 5), [0 0 1]));
fprintf('CHK|fs_n|%d|exact\n', sol.stats.nmeshpoints);
fprintf('CHK|fs_nodeevals|%d|exact\n', sol.stats.nODEevals);
fprintf('CHK|fs_wallshear|%.17g|rel=1e-6\n', sol.y(3, 1));
for bnew = 4:6
    solinit = bvpxtend(sol, bnew);
    fprintf('CHK|fs_xtend_n%d|%d|exact\n', bnew, numel(solinit.x));
    fprintf('CHK|fs_xtend_x%d|%.17g|exact\n', bnew, solinit.x(end));
    fprintf('CHK|fs_xtend_y%d|%.17g|rel=1e-12\n', bnew, solinit.y(2, end));
    sol = bvp4c(@fsode, @fsbc, solinit);
    fprintf('CHK|fs_n%d|%d|exact\n', bnew, sol.stats.nmeshpoints);
    fprintf('CHK|fs_wallshear%d|%.17g|rel=1e-6\n', bnew, sol.y(3, 1));
end

% bvpxtend's three extrapolations, and the parameter it can replace
base = bvp4c(@twoode, @twobc, bvpinit(linspace(0, 4, 5), [1 0]));
for method = {'constant', 'linear', 'solution'}
    ext = bvpxtend(base, 5, method{1});
    fprintf('CHK|xtend_%s_n|%d|exact\n', method{1}, numel(ext.x));
    fprintf('CHK|xtend_%s_y|%.17g|rel=1e-10\n', method{1}, ext.y(1, end));
    fprintf('CHK|xtend_%s_yp|%.17g|rel=1e-10\n', method{1}, ext.yp(1, end));
    left = bvpxtend(base, -1, method{1});
    fprintf('CHK|xtend_left_%s_y|%.17g|rel=1e-10\n', method{1}, left.y(1, 1));
end
same = bvpxtend(base, 4 + 1e-15);
fprintf('CHK|xtend_same_n|%d|exact\n', numel(same.x));
fprintf('CHK|xtend_same_x|%.17g|rel=1e-12\n', same.x(end));

% --- emdenbvp: a singular BVP, y' = S*y/x + f(x, y) --------------------------------------------
S = [0 0; 0 -2];
opts = bvpset('SingularTerm', S);
sol = bvp4c(@emdenode, @emdenbc, bvpinit(linspace(0, 1, 5), [sqrt(3) / 2; 0]), opts);
fprintf('CHK|emden_n|%d|exact\n', sol.stats.nmeshpoints);
fprintf('CHK|emden_maxres|%.17g|rel=1e-6\n', sol.stats.maxres);
fprintf('CHK|emden_nodeevals|%d|exact\n', sol.stats.nODEevals);
fprintf('CHK|emden_y0|%.17g|rel=1e-8\n', sol.y(1, 1));
fprintf('CHK|emden_yend|%.17g|rel=1e-10\n', sol.y(1, end));
w = deval(sol, 0.5);
fprintf('CHK|emden_deval|%.17g|rel=1e-6\n', w(1));
sol5 = bvp5c(@emdenode, @emdenbc, bvpinit(linspace(0, 1, 5), [sqrt(3) / 2; 0]), opts);
fprintf('CHK|emden_5c_n|%d|exact\n', sol5.stats.nmeshpoints);
fprintf('CHK|emden_5c_maxerr|%.17g|rel=1e-6\n', sol5.stats.maxerr);
fprintf('CHK|emden_5c_y0|%.17g|rel=1e-8\n', sol5.y(1, 1));

% --- rcbvp: Russell and Christiansen's example C, with an analytic Jacobian and crude tolerances
a = 1 / (3 * pi);
opts = bvpset('FJacobian', @rcjac, 'RelTol', 0.1, 'AbsTol', 0.1);
sol4 = bvp4c(@rcode, @rcbc, bvpinit(linspace(a, 1, 10), [1; 1]), opts);
sol5 = bvp5c(@rcode, @rcbc, bvpinit(linspace(a, 1, 10), [1; 1]), opts);
fprintf('CHK|rc_4c_n|%d|exact\n', sol4.stats.nmeshpoints);
fprintf('CHK|rc_4c_maxres|%.17g|rel=1e-6\n', sol4.stats.maxres);
fprintf('CHK|rc_4c_nodeevals|%d|exact\n', sol4.stats.nODEevals);
fprintf('CHK|rc_4c_nbcevals|%d|exact\n', sol4.stats.nBCevals);
fprintf('CHK|rc_4c_slope|%.17g|rel=1e-6\n', sol4.y(2, 1));
fprintf('CHK|rc_5c_n|%d|exact\n', sol5.stats.nmeshpoints);
fprintf('CHK|rc_5c_maxerr|%.17g|rel=1e-6\n', sol5.stats.maxerr);
fprintf('CHK|rc_5c_nodeevals|%d|exact\n', sol5.stats.nODEevals);
fprintf('CHK|rc_5c_slope|%.17g|rel=1e-6\n', sol5.y(2, 1));

% --- shockbvp: a boundary layer at x = 0, vectorized, with both Jacobians supplied --------------
sol = bvpinit([-1 -0.5 0 0.5 1], [1 0]);
for i = 2:4
    e = 0.1 / 10^(i - 1);
    opts = bvpset('FJacobian', @(x, y) shockjac(x, y, e), ...
        'BCJacobian', {[1 0; 0 0], [0 0; 1 0]}, 'Vectorized', 'on');
    sol = bvp4c(@(x, y) shockode(x, y, e), @shockbc, sol, opts);
    fprintf('CHK|shock_n%d|%d|exact\n', i, sol.stats.nmeshpoints);
    fprintf('CHK|shock_nodeevals%d|%d|exact\n', i, sol.stats.nODEevals);
    fprintf('CHK|shock_nbcevals%d|%d|exact\n', i, sol.stats.nBCevals);
end
fprintf('CHK|shock_maxres|%.17g|rel=1e-6\n', sol.stats.maxres);
fprintf('CHK|shock_ya|%.17g|rel=1e-10\n', sol.y(1, 1));
fprintf('CHK|shock_slope|%.17g|rel=1e-4\n', sol.y(2, 1));
w = deval(sol, 0.25);
fprintf('CHK|shock_deval|%.17g|abs=1e-8\n', w(1));

% --- threebvp: a three-point BVP, solved by continuation in kappa -------------------------------
xinit = [0, 0.25, 0.5, 0.75, 1, 1, 1.25, 1.5, 1.75, 2];
sol = bvpinit(xinit, [1; 1]);
fprintf('CHK|three_init_n|%d|exact\n', numel(sol.x));
for kappa = 2:5
    sol = bvp4c(@(x, y, region) threeode(x, y, region, kappa), @threebc, sol);
    fprintf('CHK|three_n%d|%d|exact\n', kappa, sol.stats.nmeshpoints);
    fprintf('CHK|three_os%d|%.17g|rel=1e-6\n', kappa, 1 / sol.y(1, end));
end
fprintf('CHK|three_nodeevals|%d|exact\n', sol.stats.nODEevals);
fprintf('CHK|three_nbcevals|%d|exact\n', sol.stats.nBCevals);
fprintf('CHK|three_interfaces|%d|exact\n', sum(diff(sol.x) == 0));
sol5 = bvp5c(@(x, y, region) threeode(x, y, region, 5), @threebc, bvpinit(xinit, [1; 1]));
fprintf('CHK|three_5c_n|%d|exact\n', sol5.stats.nmeshpoints);
fprintf('CHK|three_5c_os|%.17g|rel=1e-6\n', 1 / sol5.y(1, end));

% --- bvpset and bvpget --------------------------------------------------------------------------
o = bvpset('RelTol', 1e-4, 'AbsTol', [1e-7 1e-8], 'Stats', 'on', 'Nmax', 500);
fprintf('CHK|bvpset_fields|%d|exact\n', numel(fieldnames(o)));
fprintf('CHK|bvpset_reltol|%.17g|exact\n', bvpget(o, 'RelTol'));
fprintf('CHK|bvpset_abstol|%.17g|exact\n', bvpget(o, 'AbsTol') * [1; 1]);
fprintf('CHK|bvpset_stats|%s|exact\n', bvpget(o, 'Stats'));
fprintf('CHK|bvpset_nmax|%d|exact\n', bvpget(o, 'Nmax'));
fprintf('CHK|bvpget_default|%.17g|exact\n', bvpget(o, 'Vectorized', 7));
fprintf('CHK|bvpget_empty|%.17g|exact\n', bvpget([], 'RelTol', 3));
o2 = bvpset(o, 'RelTol', 1e-6);
fprintf('CHK|bvpset_merge|%.17g|exact\n', bvpget(o2, 'RelTol'));
fprintf('CHK|bvpset_merge_keep|%d|exact\n', bvpget(o2, 'Nmax'));
fprintf('CHK|bvpset_abbrev|%.17g|exact\n', bvpget(bvpset('rel', 0.5), 'RelTol'));

% --- bvpinit's three ways of naming a guess ----------------------------------------------------
g1 = bvpinit(linspace(0, 4, 5), [1 0]);
fprintf('CHK|bvpinit_solver|%s|exact\n', g1.solver);
fprintf('CHK|bvpinit_xshape|%s|shape\n', mat2str(size(g1.x)));
fprintf('CHK|bvpinit_yshape|%s|shape\n', mat2str(size(g1.y)));
fprintf('CHK|bvpinit_y|%.17g|exact\n', g1.y(1, 3));
g2 = bvpinit(linspace(0, pi, 4), @mat4init);
fprintf('CHK|bvpinit_fun_y|%.17g|rel=1e-14\n', g2.y(2, 3));
g3 = bvpinit(linspace(0, 1, 4), [1 2], [7 8]);
fprintf('CHK|bvpinit_par|%.17g|exact\n', g3.parameters * [1; 1]);
g4 = bvpinit(xinit, [1; 1]);
fprintf('CHK|bvpinit_mbvp_n|%d|exact\n', numel(g4.x));

% --- ddex1: Wille and Baker, two constant lags -------------------------------------------------
sol = dde23(@ddex1de, [1, 0.2], [1; 1; 1], [0, 5]);
fprintf('CHK|ddex1_solver|%s|exact\n', sol.solver);
fprintf('CHK|ddex1_n|%d|exact\n', numel(sol.x));
fprintf('CHK|ddex1_nsteps|%d|exact\n', sol.stats.nsteps);
fprintf('CHK|ddex1_nfailed|%d|exact\n', sol.stats.nfailed);
fprintf('CHK|ddex1_nfevals|%d|exact\n', sol.stats.nfevals);
fprintf('CHK|ddex1_tfinal|%.17g|exact\n', sol.stats.tfinal);
fprintf('CHK|ddex1_ndiscont|%d|exact\n', numel(sol.discont));
fprintf('CHK|ddex1_discont1|%.17g|rel=1e-14\n', sol.discont(1));
fprintf('CHK|ddex1_discontend|%.17g|rel=1e-14\n', sol.discont(end));
fprintf('CHK|ddex1_yshape|%s|shape\n', mat2str(size(sol.y)));
fprintf('CHK|ddex1_ypshape|%s|shape\n', mat2str(size(sol.yp)));
for k = 1:3
    fprintf('CHK|ddex1_y%d|%.17g|rel=1e-8\n', k, sol.y(k, end));
    fprintf('CHK|ddex1_yp%d|%.17g|rel=1e-8\n', k, sol.yp(k, end));
end
z = deval(sol, [1 2.5 4]);
fprintf('CHK|ddex1_deval_shape|%s|shape\n', mat2str(size(z)));
fprintf('CHK|ddex1_deval|%.17g|rel=1e-8\n', z(2, 2));
[v, vp] = deval(sol, 3.25);
fprintf('CHK|ddex1_deval_v|%.17g|rel=1e-8\n', v(3));
fprintf('CHK|ddex1_deval_vp|%.17g|rel=1e-8\n', vp(3));

% ddex1 again as a state-dependent problem: ddesd with delay functions of the same lags
sold = ddesd(@ddex1de, @ddex1delays, [1; 1; 1], [0, 5]);
fprintf('CHK|ddex1sd_solver|%s|exact\n', sold.solver);
fprintf('CHK|ddex1sd_nsteps|%d|exact\n', sold.stats.nsteps);
fprintf('CHK|ddex1sd_nfevals|%d|exact\n', sold.stats.nfevals);
fprintf('CHK|ddex1sd_y1|%.17g|rel=1e-8\n', sold.y(1, end));
fprintf('CHK|ddex1sd_y3|%.17g|rel=1e-8\n', sold.y(3, end));

% ddesd accepts constant lags too, which is the form the documentation says is dde23's
soll = ddesd(@ddex1de, [1, 0.2], [1; 1; 1], [0, 5]);
fprintf('CHK|ddex1sdc_nsteps|%d|exact\n', soll.stats.nsteps);
fprintf('CHK|ddex1sdc_y1|%.17g|rel=1e-8\n', soll.y(1, end));

% --- ddex2: the baroreflex problem, with a jump in the coefficients at t = 600 ------------------
p0 = 93; rr = 0.068; vstr = 67.9;
history = [p0; (1 / (1 + 1.05 / rr)) * p0; (1 / (1.05 * vstr)) * (1 / (1 + rr / 1.05)) * p0];
sol = dde23(@ddex2de, 4, history, [0, 1000], ddeset('Jumps', 600));
fprintf('CHK|ddex2_ndiscont|%d|exact\n', numel(sol.discont));
fprintf('CHK|ddex2_discontend|%.17g|rel=1e-12\n', sol.discont(end));
fprintf('CHK|ddex2_tfinal|%.17g|exact\n', sol.stats.tfinal);
fprintf('CHK|ddex2_history|%.17g|exact\n', sol.history(1));
fprintf('CHK|ddex2_nsteps|%d|div=ADR0135\n', sol.stats.nsteps);
fprintf('CHK|ddex2_heartrate|%.17g|div=ADR0135\n', sol.y(3, end));

% --- ddex3: the D1 problem of Enright and Hayashi, a state-dependent delay ----------------------
sol = ddesd(@ddex3de, @ddex3delay, @ddex3hist, [0.1, 5]);
fprintf('CHK|ddex3_solver|%s|exact\n', sol.solver);
fprintf('CHK|ddex3_n|%d|exact\n', numel(sol.x));
fprintf('CHK|ddex3_nsteps|%d|exact\n', sol.stats.nsteps);
fprintf('CHK|ddex3_nfailed|%d|exact\n', sol.stats.nfailed);
fprintf('CHK|ddex3_nfevals|%d|exact\n', sol.stats.nfevals);
fprintf('CHK|ddex3_y1|%.17g|rel=1e-8\n', sol.y(1, end));
fprintf('CHK|ddex3_y2|%.17g|rel=1e-8\n', sol.y(2, end));
fprintf('CHK|ddex3_exact|%.17g|abs=1e-4\n', sol.y(1, end) - log(5));
z = deval(sol, [1 2 3 4]);
fprintf('CHK|ddex3_deval|%.17g|rel=1e-8\n', z(1, 3));

% --- ddex4: a neutral problem of Paul, two delay functions --------------------------------------
sol = ddensd(@ddex4de, @ddex4ydel, @ddex4ypdel, @ddex4hist, [0, pi]);
fprintf('CHK|ddex4_solver|%s|exact\n', sol.solver);
fprintf('CHK|ddex4_n|%d|exact\n', numel(sol.x));
fprintf('CHK|ddex4_nsteps|%d|exact\n', sol.stats.nsteps);
fprintf('CHK|ddex4_nfailed|%d|exact\n', sol.stats.nfailed);
fprintf('CHK|ddex4_nfevals|%d|exact\n', sol.stats.nfevals);
fprintf('CHK|ddex4_yend|%.17g|rel=1e-6\n', sol.y(1, end));
fprintf('CHK|ddex4_exact|%.17g|abs=1e-2\n', sol.y(1, end) + 1);
fprintf('CHK|ddex4_ivp|%d|exact\n', sol.IVP);
% The midpoint of this run sits at a zero of cos, so the value there is four orders of magnitude
% below the solution's own size; a neutral solver reaches it through a difference quotient over a
% sqrt(eps) interval, and the last digits of that quotient are rounding. Pinned as an absolute
% distance rather than a relative one for that reason: the two engines differ here by 6e-10.
z = deval(sol, linspace(0, pi, 5));
fprintf('CHK|ddex4_deval|%.17g|abs=1e-8\n', z(3));

% --- ddex5: Jackiewicz' initial-value neutral problem, two consistent slopes --------------------
for slope = [2, 0.4063757399599599]
    sol = ddensd(@ddex5de, @ddex5delay, @ddex5delay, {1, slope}, [0, 0.1]);
    tag = sprintf('ddex5_%d', round(slope * 100));
    fprintf('CHK|%s_n|%d|exact\n', tag, numel(sol.x));
    fprintf('CHK|%s_nsteps|%d|exact\n', tag, sol.stats.nsteps);
    fprintf('CHK|%s_nfevals|%d|exact\n', tag, sol.stats.nfevals);
    fprintf('CHK|%s_yend|%.17g|rel=1e-8\n', tag, sol.y(1, end));
    fprintf('CHK|%s_ivp|%d|exact\n', tag, sol.IVP);
end

% --- ddeset and ddeget --------------------------------------------------------------------------
d = ddeset('RelTol', 1e-5, 'AbsTol', 1e-9, 'Jumps', [1 2 3], 'InitialY', [4; 5], 'Stats', 'off');
fprintf('CHK|ddeset_fields|%d|exact\n', numel(fieldnames(d)));
fprintf('CHK|ddeset_reltol|%.17g|exact\n', ddeget(d, 'RelTol'));
fprintf('CHK|ddeset_jumps|%.17g|exact\n', ddeget(d, 'Jumps') * [1; 1; 1]);
w = ddeget(d, 'InitialY');
fprintf('CHK|ddeset_initialy|%.17g|exact\n', w(1) + w(2));
fprintf('CHK|ddeget_default|%.17g|exact\n', ddeget(d, 'MaxStep', 11));
fprintf('CHK|ddeget_empty|%.17g|exact\n', ddeget([], 'RelTol', 2));
fprintf('CHK|ddeset_abbrev|%.17g|exact\n', ddeget(ddeset('max', 0.25), 'MaxStep'));

% --- the options dde23 acts on: InitialY, a tighter tolerance, and a capped step ----------------
sol = dde23(@ddex1de, [1, 0.2], [1; 1; 1], [0, 5], ddeset('InitialY', [2; 1; 1]));
fprintf('CHK|ddeopt_initialy_nsteps|%d|exact\n', sol.stats.nsteps);
fprintf('CHK|ddeopt_initialy_y0|%.17g|exact\n', sol.y(1, 1));
fprintf('CHK|ddeopt_initialy_yend|%.17g|rel=1e-8\n', sol.y(1, end));
sol = dde23(@ddex1de, [1, 0.2], [1; 1; 1], [0, 5], ddeset('RelTol', 1e-8, 'AbsTol', 1e-10));
fprintf('CHK|ddeopt_tight_nsteps|%d|exact\n', sol.stats.nsteps);
fprintf('CHK|ddeopt_tight_yend|%.17g|rel=1e-10\n', sol.y(1, end));
sol = dde23(@ddex1de, [1, 0.2], [1; 1; 1], [0, 5], ddeset('MaxStep', 0.1));
fprintf('CHK|ddeopt_maxstep_nsteps|%d|exact\n', sol.stats.nsteps);
fprintf('CHK|ddeopt_maxstep_hmax|%.17g|rel=1e-12\n', max(diff(sol.x)));
sol = dde23(@ddex1de, [1, 0.2], [1; 1; 1], [0, 5], ddeset('InitialStep', 0.01));
fprintf('CHK|ddeopt_h0|%.17g|rel=1e-12\n', sol.x(2) - sol.x(1));

% --- events on a delay problem ------------------------------------------------------------------
sol = dde23(@ddex1de, [1, 0.2], [1; 1; 1], [0, 5], ddeset('Events', @ddex1event));
fprintf('CHK|ddeevent_count|%d|exact\n', numel(sol.xe));
fprintf('CHK|ddeevent_xe|%.17g|rel=1e-8\n', sol.xe(1));
fprintf('CHK|ddeevent_ie|%d|exact\n', sol.ie(1));
fprintf('CHK|ddeevent_ye|%.17g|rel=1e-8\n', sol.ye(1, 1));
fprintf('CHK|ddeevent_end|%.17g|rel=1e-8\n', sol.x(end));

% --- continuing a delay solution: the solution itself as the history ---------------------------
first = dde23(@ddex1de, [1, 0.2], [1; 1; 1], [0, 3]);
second = dde23(@ddex1de, [1, 0.2], first, [3, 5]);
fprintf('CHK|ddecont_n|%d|exact\n', numel(second.x));
fprintf('CHK|ddecont_x1|%.17g|exact\n', second.x(1));
fprintf('CHK|ddecont_yend|%.17g|rel=1e-6\n', second.y(1, end));
fprintf('CHK|ddecont_ndiscont|%d|exact\n', numel(second.discont));

% --- deval refuses what it should ---------------------------------------------------------------
sol = bvp4c(@twoode, @twobc, bvpinit(linspace(0, 4, 5), [1 0]));
try
    deval(sol, 5);
    fprintf('CHK|deval_outside|accepted|exact\n');
catch
    fprintf('CHK|deval_outside|refused|exact\n');
end
w = deval(2, sol);
fprintf('CHK|deval_swapped|%.17g|rel=1e-10\n', w(1));
fprintf('CHK|deval_idx|%.17g|rel=1e-10\n', deval(sol, 2, 2));

% --- local functions ----------------------------------------------------------------------------

function dydx = twoode(x, y) %#ok<INUSL>
dydx = [y(2); -abs(y(1))];
end

function res = twobc(ya, yb)
res = [ya(1); yb(1) + 2];
end

function dydx = mat4ode(x, y, lambda)
q = 5;
dydx = [y(2); -(lambda - 2 * q * cos(2 * x)) * y(1)];
end

function res = mat4bc(ya, yb, lambda) %#ok<INUSD>
res = [ya(2); yb(2); ya(1) - 1];
end

function yinit = mat4init(x)
yinit = [cos(4 * x); -4 * sin(4 * x)];
end

function dfdeta = fsode(eta, f) %#ok<INUSL>
beta = 0.5;
dfdeta = [f(2); f(3); -f(1) * f(3) - beta * (1 - f(2)^2)];
end

function res = fsbc(f0, finf)
res = [f0(1); f0(2); finf(2) - 1];
end

function dydx = emdenode(x, y) %#ok<INUSL>
dydx = [y(2); -y(1)^5];
end

function res = emdenbc(ya, yb)
res = [ya(2); yb(1) - sqrt(3) / 2];
end

function dydx = rcode(x, y)
dydx = [y(2); -2 * y(2) / x - y(1) / x^4];
end

function dfdy = rcjac(x, y) %#ok<INUSD>
dfdy = [0, 1; -1 / x^4, -2 / x];
end

function res = rcbc(ya, yb)
res = [ya(1); yb(1) - sin(1)];
end

function dydx = shockode(x, y, e)
pix = pi * x;
dydx = [y(2, :); -x / e .* y(2, :) - pi^2 * cos(pix) - pix / e .* sin(pix)];
end

function res = shockbc(ya, yb)
res = [ya(1) + 2; yb(1)];
end

function j = shockjac(x, y, e) %#ok<INUSL>
j = [0 1; 0 -x / e];
end

function dydx = threeode(x, y, region, kappa)
n = 5e-2;
lambda = 2;
eta = lambda^2 / (n * kappa^2);
dydx = zeros(2, 1);
dydx(1) = (y(2) - 1) / n;
if region == 1
    dydx(2) = (y(1) * y(2) - x) / eta;
else
    dydx(2) = (y(1) * y(2) - 1) / eta;
end
end

function res = threebc(yl, yr)
res = [yl(1, 1); yr(1, 1) - yl(1, 2); yr(2, 1) - yl(2, 2); yr(2, end) - 1];
end

function dydt = ddex1de(t, y, z) %#ok<INUSL>
ylag1 = z(:, 1);
ylag2 = z(:, 2);
dydt = [ylag1(1); ylag1(1) + ylag2(2); y(2)];
end

function d = ddex1delays(t, y) %#ok<INUSD>
d = [t - 1; t - 0.2];
end

function [value, isterminal, direction] = ddex1event(t, y, z) %#ok<INUSD>
value = y(1) - 5;
isterminal = 0;
direction = 0;
end

function dydt = ddex2de(t, y, z)
ca = 1.55; cv = 519; r = 0.068; vstr = 67.9;
alphas = 93; alphap = 93; betas = 7; betap = 7;
alphah = 0.84; betah = 1.17; gammah = 0;
if t <= 600
    rr = 1.05;
else
    rr = 0.21 * exp(600 - t) + 0.84;
end
patau = z(1, 1);
paoft = y(1); pvoft = y(2); hoft = y(3);
dpadt = -(1 / (ca * rr)) * paoft + (1 / (ca * rr)) * pvoft + (1 / ca) * vstr * hoft;
dpvdt = (1 / (cv * rr)) * paoft - (1 / (cv * rr) + 1 / (cv * r)) * pvoft;
ts = 1 / (1 + (patau / alphas)^betas);
tp = 1 / (1 + (alphap / paoft)^betap);
dhdt = (alphah * ts) / (1 + gammah * tp) - betah * tp;
dydt = [dpadt; dpvdt; dhdt];
end

function v = ddex3hist(t)
v = [log(t); 1 ./ t];
end

function d = ddex3delay(t, y) %#ok<INUSL>
d = exp(1 - y(2));
end

function dydt = ddex3de(t, y, z) %#ok<INUSL>
dydt = [y(2); -z(2) * y(2)^2 * exp(1 - y(2))];
end

function v = ddex4hist(t)
v = cos(t);
end

function del = ddex4ydel(t, y) %#ok<INUSD>
del = t / 2;
end

function del = ddex4ypdel(t, y) %#ok<INUSD>
del = t - pi;
end

function dydt = ddex4de(t, y, ydel, ypdel) %#ok<INUSL>
dydt = 1 + y - 2 * ydel^2 - ypdel;
end

function del = ddex5delay(t, y) %#ok<INUSD>
del = t / 2;
end

function dydt = ddex5de(t, y, ydel, ypdel) %#ok<INUSL>
dydt = 2 * cos(2 * t) * ydel^(2 * cos(t)) + log(ypdel) - log(2 * cos(t)) - sin(t);
end
