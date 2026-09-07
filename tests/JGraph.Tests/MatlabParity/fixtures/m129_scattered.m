% m129_scattered.m -- scattered data: griddata and griddatan, the two interpolant values
% scatteredInterpolant and griddedInterpolant, the N-D tessellation verbs delaunayn, tsearchn,
% dsearchn and convhulln, the shrinking hull boundary, and the STL pair.
%
% MATLAB's tessellation is Qhull's and this one is Bowyer-Watson's, so the two agree on the set of
% simplices for points in general position and not on the order they are listed in. Every line here
% is therefore pinned on something the order cannot move: an interpolated value, a total, a count,
% or a sum of indices. The points are put in general position deliberately -- a lattice of cosines
% nudged by a thousandth of a sine -- so that no four of them are co-circular and the two engines
% have the same set to disagree about the order of.
%
% griddata's 'cubic' is the one method whose surface differs. Its vertex gradients and its value
% along every triangulation edge agree with MATLAB to the last bits; its interior does not, and no
% split of the triangle makes MathWorks' surface piecewise cubic, so the element itself is not the
% one written here. Those lines are div= and say so.
n = 40;
t = (1:n)';
x = 2*cos(2.3*t) + 0.001*sin(t);
y = 2*sin(1.7*t) + 0.001*cos(t);
z = 2*cos(0.9*t);
v = x.*exp(-x.^2 - y.^2);
P = [x y];
xq = -1.5:0.75:1.5;
yq = (-1.5:0.75:1.5)';

% --- griddata: the five methods over a grid -----------------------------------------------------
methods = {'nearest', 'linear', 'natural', 'cubic', 'v4'};
for i = 1:5
    name = methods{i};
    vq = griddata(x, y, v, xq, yq, name);
    if strcmp(name, 'cubic')
        rule = 'div=ADR0134';
    elseif strcmp(name, 'v4')
        rule = 'rel=1e-6';
    else
        rule = 'rel=1e-10';
    end
    fprintf('CHK|griddata_%s_rows|%d|exact\n', name, size(vq, 1));
    fprintf('CHK|griddata_%s_cols|%d|exact\n', name, size(vq, 2));
    picks = [1 7 13 19 25];
    for k = 1:5
        fprintf('CHK|griddata_%s_%d|%.17g|%s\n', name, k, vq(picks(k)), rule);
    end
    fprintf('CHK|griddata_%s_total|%.17g|%s\n', name, sum(vq(:)), rule);
end

% The three-output form hands the grid it built back, which is the whole reason it exists.
[XQ, YQ, VQ] = griddata(x, y, v, xq, yq);
fprintf('CHK|griddata_three_xrows|%d|exact\n', size(XQ, 1));
fprintf('CHK|griddata_three_xcols|%d|exact\n', size(XQ, 2));
fprintf('CHK|griddata_three_x12|%.17g|exact\n', XQ(1, 2));
fprintf('CHK|griddata_three_y21|%.17g|exact\n', YQ(2, 1));
fprintf('CHK|griddata_three_vtotal|%.17g|rel=1e-10\n', sum(VQ(:)));

% Query arrays of one shape are read point by point rather than expanded into a grid.
vp = griddata(x, y, v, [0.1 -0.3 0.4], [0.2 0.35 -0.15]);
fprintf('CHK|griddata_points_rows|%d|exact\n', size(vp, 1));
fprintf('CHK|griddata_points_cols|%d|exact\n', size(vp, 2));
for k = 1:3
    fprintf('CHK|griddata_points_%d|%.17g|rel=1e-10\n', k, vp(k));
end

% --- griddata in space --------------------------------------------------------------------------
v3 = x + y - z;
d3 = -0.5:0.5:0.5;
[A3, B3, C3] = meshgrid(d3, d3, 0);
for i = 1:2
    name = methods{i};
    w3 = griddata(x, y, z, v3, A3, B3, C3, name);
    fprintf('CHK|griddata3_%s_n|%d|exact\n', name, numel(w3));
    % v3 is linear in the coordinates, so both methods reproduce it exactly and what the total
    % holds is the rounding left over; an absolute rule is the only one that means anything there.
    fprintf('CHK|griddata3_%s_total|%.17g|abs=1e-12\n', name, sum(w3(:)));
    fprintf('CHK|griddata3_%s_mid|%.17g|abs=1e-12\n', name, w3(2, 2));
end

% --- delaunayn: the set, not the order ----------------------------------------------------------
T = delaunayn(P);
fprintf('CHK|delaunayn_rows|%d|exact\n', size(T, 1));
fprintf('CHK|delaunayn_cols|%d|exact\n', size(T, 2));
fprintf('CHK|delaunayn_indexsum|%d|exact\n', sum(T(:)));
degree = zeros(n, 1);
for i = 1:size(T, 1)
    degree(T(i, :)) = degree(T(i, :)) + 1;
end
fprintf('CHK|delaunayn_degreesum|%d|exact\n', sum(degree));
fprintf('CHK|delaunayn_degreemax|%d|exact\n', max(degree));
fprintf('CHK|delaunayn_degree1|%d|exact\n', degree(1));
fprintf('CHK|delaunayn_degree20|%d|exact\n', degree(20));
area = 0;
for i = 1:size(T, 1)
    a = T(i, 1); b = T(i, 2); c = T(i, 3);
    area = area + abs((x(b)-x(a))*(y(c)-y(a)) - (y(b)-y(a))*(x(c)-x(a)))/2;
end
fprintf('CHK|delaunayn_area|%.17g|rel=1e-10\n', area);

P3 = [x y z];
T3 = delaunayn(P3);
fprintf('CHK|delaunayn3_cols|%d|exact\n', size(T3, 2));
volume = 0;
for i = 1:size(T3, 1)
    a = T3(i, 1); b = T3(i, 2); c = T3(i, 3); d = T3(i, 4);
    m = [x(b)-x(a) y(b)-y(a) z(b)-z(a); x(c)-x(a) y(c)-y(a) z(c)-z(a); x(d)-x(a) y(d)-y(a) z(d)-z(a)];
    volume = volume + abs(det(m))/6;
end
fprintf('CHK|delaunayn3_volume|%.17g|rel=1e-10\n', volume);

% --- tsearchn: the simplex a point lands in, read through its barycentric coordinates ------------
XI = [0 0; 1 1; 5 5; -0.5 0.3; 0.8 -1.1];
[ts, bary] = tsearchn(P, T, XI);
fprintf('CHK|tsearchn_rows|%d|exact\n', size(ts, 1));
fprintf('CHK|tsearchn_barycols|%d|exact\n', size(bary, 2));
for k = 1:5
    fprintf('CHK|tsearchn_nan_%d|%d|exact\n', k, double(isnan(ts(k))));
    if isnan(ts(k))
        fprintf('CHK|tsearchn_value_%d|%s|exact\n', k, 'outside');
    else
        % The index differs between the engines because the simplex lists differ in order; the
        % value the coordinates reconstruct does not.
        fprintf('CHK|tsearchn_value_%d|%.17g|rel=1e-10\n', k, bary(k, :) * v(T(ts(k), :)));
        fprintf('CHK|tsearchn_weight_%d|%.17g|abs=1e-12\n', k, sum(bary(k, :)));
    end
end

% --- dsearchn ------------------------------------------------------------------------------------
[ks, ds] = dsearchn(P, T, XI);
for k = 1:5
    fprintf('CHK|dsearchn_k_%d|%d|exact\n', k, ks(k));
    fprintf('CHK|dsearchn_d_%d|%.17g|rel=1e-12\n', k, ds(k));
end
k2 = dsearchn(P, XI);
fprintf('CHK|dsearchn_two_arg|%d|exact\n', sum(k2));
k3 = dsearchn(P, T, XI, Inf);
fprintf('CHK|dsearchn_outval_finite|%d|exact\n', sum(isfinite(k3)));
fprintf('CHK|dsearchn_outval_sum|%.17g|exact\n', sum(k3(isfinite(k3))));

% --- convhulln -------------------------------------------------------------------------------------
[K, V] = convhulln(P);
fprintf('CHK|convhulln_rows|%d|exact\n', size(K, 1));
fprintf('CHK|convhulln_cols|%d|exact\n', size(K, 2));
fprintf('CHK|convhulln_area|%.17g|rel=1e-10\n', V);
fprintf('CHK|convhulln_indexsum|%d|exact\n', sum(K(:)));
[K3, V3] = convhulln(P3);
fprintf('CHK|convhulln3_cols|%d|exact\n', size(K3, 2));
fprintf('CHK|convhulln3_volume|%.17g|rel=1e-10\n', V3);
fprintf('CHK|convhulln3_indexsum|%d|exact\n', sum(sum(sort(K3, 2))));

% --- griddatan ------------------------------------------------------------------------------------
yi = griddatan(P, v, XI);
fprintf('CHK|griddatan_rows|%d|exact\n', size(yi, 1));
for k = 1:5
    if isnan(yi(k))
        fprintf('CHK|griddatan_%d|%s|exact\n', k, 'outside');
    else
        fprintf('CHK|griddatan_%d|%.17g|rel=1e-10\n', k, yi(k));
    end
end
yn = griddatan(P, v, XI, 'nearest');
fprintf('CHK|griddatan_nearest_total|%.17g|rel=1e-10\n', sum(yn(isfinite(yn))));

% --- boundary --------------------------------------------------------------------------------------
shrinks = [0 0.5 1];
for i = 1:3
    s = shrinks(i);
    [KB, VB] = boundary(x, y, s);
    fprintf('CHK|boundary_%d_count|%d|exact\n', i, numel(KB));
    fprintf('CHK|boundary_%d_area|%.17g|rel=1e-10\n', i, VB);
    fprintf('CHK|boundary_%d_closed|%d|exact\n', i, double(KB(1) == KB(end)));
    fprintf('CHK|boundary_%d_indexsum|%d|exact\n', i, sum(KB));
end
kb = boundary(P);
fprintf('CHK|boundary_matrix_form|%d|exact\n', numel(kb));
[KB3, VB3] = boundary(x, y, z, 0.5);
fprintf('CHK|boundary3_cols|%d|exact\n', size(KB3, 2));
fprintf('CHK|boundary3_volume|%.17g|rel=1e-10\n', VB3);

% --- scatteredInterpolant -----------------------------------------------------------------------
F = scatteredInterpolant(x, y, v);
fprintf('CHK|scattered_class|%s|exact\n', class(F));
fprintf('CHK|scattered_method|%s|exact\n', F.Method);
fprintf('CHK|scattered_extrap|%s|exact\n', F.ExtrapolationMethod);
fprintf('CHK|scattered_points_rows|%d|exact\n', size(F.Points, 1));
fprintf('CHK|scattered_points_cols|%d|exact\n', size(F.Points, 2));
fprintf('CHK|scattered_values_rows|%d|exact\n', size(F.Values, 1));
fprintf('CHK|scattered_values_cols|%d|exact\n', size(F.Values, 2));
fprintf('CHK|scattered_at_point|%.17g|rel=1e-10\n', F(0.1, 0.2));
gq = F(XQ, YQ);
fprintf('CHK|scattered_grid_rows|%d|exact\n', size(gq, 1));
fprintf('CHK|scattered_grid_total|%.17g|rel=1e-10\n', sum(gq(:)));
pq = F([0.1 -0.3; 0.4 0.2]);
fprintf('CHK|scattered_matrix_rows|%d|exact\n', size(pq, 1));
fprintf('CHK|scattered_matrix_cols|%d|exact\n', size(pq, 2));
fprintf('CHK|scattered_matrix_1|%.17g|rel=1e-10\n', pq(1));

% Replacing the values re-uses the triangulation, which is what makes the answer scale.
F.Values = 2*v;
fprintf('CHK|scattered_after_set|%.17g|rel=1e-10\n', F(0.1, 0.2));
F.Values = v;
F.Method = 'natural';
fprintf('CHK|scattered_natural|%.17g|rel=1e-10\n', F(0.1, 0.2));
F.Method = 'nearest';
fprintf('CHK|scattered_nearest|%.17g|rel=1e-12\n', F(0.1, 0.2));

Fnat = scatteredInterpolant(x, y, v, 'natural');
fprintf('CHK|scattered_natural_extrap|%s|exact\n', Fnat.ExtrapolationMethod);
Fnear = scatteredInterpolant(x, y, v, 'nearest');
fprintf('CHK|scattered_nearest_extrap|%s|exact\n', Fnear.ExtrapolationMethod);
Fnone = scatteredInterpolant(x, y, v, 'linear', 'none');
fprintf('CHK|scattered_outside_none|%d|exact\n', double(isnan(Fnone(5, 5))));
Fnn = scatteredInterpolant(x, y, v, 'linear', 'nearest');
fprintf('CHK|scattered_outside_nearest|%.17g|rel=1e-12\n', Fnn(5, 5));
Flin = scatteredInterpolant(x, y, v, 'linear', 'linear');
fprintf('CHK|scattered_outside_linear|%.17g|div=ADR0134\n', Flin(5, 5));

F3 = scatteredInterpolant(x, y, z, v3);
fprintf('CHK|scattered3_points_cols|%d|exact\n', size(F3.Points, 2));
fprintf('CHK|scattered3_at_point|%.17g|abs=1e-12\n', F3(0.1, 0.2, 0.3));

% --- griddedInterpolant ---------------------------------------------------------------------------
xg = [1 2 4 7 11];
vg = [3 1 4 1 5];
G = griddedInterpolant(xg, vg);
fprintf('CHK|gridded_class|%s|exact\n', class(G));
fprintf('CHK|gridded_method|%s|exact\n', G.Method);
fprintf('CHK|gridded_extrap|%s|exact\n', G.ExtrapolationMethod);
fprintf('CHK|gridded_vectors|%d|exact\n', numel(G.GridVectors));
fprintf('CHK|gridded_values_rows|%d|exact\n', size(G.Values, 1));
fprintf('CHK|gridded_at|%.17g|rel=1e-12\n', G(3));
gr = G([1.5 3.5]);
fprintf('CHK|gridded_row_rows|%d|exact\n', size(gr, 1));
fprintf('CHK|gridded_row_cols|%d|exact\n', size(gr, 2));
gc = G({[1.5 3.5]});
fprintf('CHK|gridded_cell_rows|%d|exact\n', size(gc, 1));
fprintf('CHK|gridded_cell_cols|%d|exact\n', size(gc, 2));
G.Values = 2*vg;
fprintf('CHK|gridded_after_set|%.17g|rel=1e-12\n', G(3));

names = {'linear', 'nearest', 'next', 'previous', 'pchip', 'makima', 'spline'};
for i = 1:7
    Gi = griddedInterpolant(xg, vg, names{i});
    fprintf('CHK|gridded_1d_%s|%.17g|rel=1e-10\n', names{i}, Gi(3.3));
    fprintf('CHK|gridded_1d_%s_extrap|%s|exact\n', names{i}, Gi.ExtrapolationMethod);
end

Gn = griddedInterpolant(xg, vg, 'linear', 'none');
fprintf('CHK|gridded_outside_none|%d|exact\n', double(isnan(Gn(20))));
Gnr = griddedInterpolant(xg, vg, 'linear', 'nearest');
fprintf('CHK|gridded_outside_nearest|%.17g|exact\n', Gnr(20));
Gl = griddedInterpolant(xg, vg, 'linear', 'linear');
fprintf('CHK|gridded_outside_linear|%.17g|rel=1e-12\n', Gl(20));

[Xg, Yg] = ndgrid(1:4, 1:3);
Vg = Xg.^2 + Yg;
G2 = griddedInterpolant(Xg, Yg, Vg);
fprintf('CHK|gridded2_vectors|%d|exact\n', numel(G2.GridVectors));
fprintf('CHK|gridded2_at|%.17g|rel=1e-12\n', G2(2.5, 1.5));
g2c = G2({[1.5 2.5 3.5], [1.5 2.5]});
fprintf('CHK|gridded2_cell_rows|%d|exact\n', size(g2c, 1));
fprintf('CHK|gridded2_cell_cols|%d|exact\n', size(g2c, 2));
fprintf('CHK|gridded2_cell_total|%.17g|rel=1e-12\n', sum(g2c(:)));
for nm = {'nearest', 'cubic', 'spline'}
    G2i = griddedInterpolant(Xg, Yg, Vg, nm{1});
    fprintf('CHK|gridded2_%s|%.17g|rel=1e-10\n', nm{1}, G2i(2.3, 1.4));
end
Gimplied = griddedInterpolant(Vg);
fprintf('CHK|gridded_implied|%.17g|rel=1e-12\n', Gimplied(2.5, 1.5));

% --- the STL pair ----------------------------------------------------------------------------------
% stlwrite takes a triangulation; that class is not built here, and a structure with the same two
% properties is what stands in for it, so the fixture makes whichever the engine will accept.
Pts = [0 0 0; 1 0 0; 1 1 0; 0 1 0; 0.5 0.5 1];
CL = [1 2 3; 1 3 4; 1 2 5; 2 3 5; 3 4 5; 4 1 5];
madeClass = 'struct';
try
    TR = triangulation(CL, Pts);
    madeClass = class(TR);
catch
    TR = struct('Points', Pts, 'ConnectivityList', CL);
end
fprintf('CHK|stl_input_class|%s|div=ADR0134\n', madeClass);
binaryFile = 'm129_roundtrip_bin.stl';
textFile = 'm129_roundtrip_txt.stl';
stlwrite(TR, binaryFile);
stlwrite(TR, textFile, 'text');
[S1, f1, a1, s1] = stlread(binaryFile);
[S2, f2] = stlread(textFile);
fprintf('CHK|stl_binary_format|%s|exact\n', f1);
fprintf('CHK|stl_text_format|%s|exact\n', f2);
fprintf('CHK|stl_binary_points|%d|exact\n', size(S1.Points, 1));
fprintf('CHK|stl_binary_faces|%d|exact\n', size(S1.ConnectivityList, 1));
fprintf('CHK|stl_binary_pointsum|%.17g|rel=1e-12\n', sum(S1.Points(:)));
fprintf('CHK|stl_binary_facesum|%d|exact\n', sum(S1.ConnectivityList(:)));
fprintf('CHK|stl_binary_attributes|%d|exact\n', numel(a1));
fprintf('CHK|stl_binary_solids|%d|exact\n', numel(s1));
fprintf('CHK|stl_text_points|%d|exact\n', size(S2.Points, 1));
fprintf('CHK|stl_text_faces|%d|exact\n', size(S2.ConnectivityList, 1));
fprintf('CHK|stl_text_pointsum|%.17g|rel=1e-12\n', sum(S2.Points(:)));
fprintf('CHK|stl_text_facesum|%d|exact\n', sum(S2.ConnectivityList(:)));
delete(binaryFile);
delete(textFile);

% --- what each engine refuses ----------------------------------------------------------------------
try
    griddata(x, y, z, v3, A3, B3, C3, 'cubic');
    fprintf('CHK|griddata_cubic_3d|%s|exact\n', 'accepted');
catch
    fprintf('CHK|griddata_cubic_3d|%s|exact\n', 'refused');
end
try
    griddata(x, y, v, xq, yq, 'quintic');
    fprintf('CHK|griddata_bad_method|%s|exact\n', 'accepted');
catch
    fprintf('CHK|griddata_bad_method|%s|exact\n', 'refused');
end
try
    F([0.1 -0.3], [0.2; 0.4]);
    fprintf('CHK|scattered_mixed_shapes|%s|exact\n', 'accepted');
catch
    fprintf('CHK|scattered_mixed_shapes|%s|exact\n', 'refused');
end
