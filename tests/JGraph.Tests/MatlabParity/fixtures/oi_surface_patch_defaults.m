% record: -noFigureWindows
% Open item 314 (ADR 0361): what a plain surface, surf, mesh and pcolor read back when made from a
% matrix, a pair of vectors and a meshgrid pair (edge colour and width, positions counted from 1,
% the position and colour modes, the matrices kept, a mesh's lighting), and a patch's colour data
% while it is unset or set by values. Probe sf1 (agent V's scratch).
close all force

f = figure('Visible', 'off');
Z = [1 2 3; 4 5 6];
C = [6 5 4; 3 2 1];
[X, Y] = meshgrid(1:3, 10:11);
props = {'EdgeColor', 'FaceColor', 'LineWidth', 'XData', 'YData', 'ZData', 'CData', ...
    'XDataMode', 'YDataMode', 'CDataMode', 'FaceLighting', 'EdgeLighting'};
cla; dump('s_Z', surface(Z), props);
cla; dump('s_ZC', surface(Z, C), props);
cla; dump('s_XYZ', surface(X, Y, Z), props);
cla; dump('s_xyZ', surface(1:3, 10:11, Z), props);
cla; dump('s_XYZC', surface(X, Y, Z, C), props);
cla; dump('s_xyZC', surface(1:3, 10:11, Z, C), props);
cla; dump('s_Z_flat', surface(Z, 'EdgeColor', 'flat'), {'EdgeColor', 'LineWidth'});
cla; dump('s_Z_wide', surface(Z, 'LineWidth', 2), {'EdgeColor', 'LineWidth'});
cla; dump('surf_Z', surf(Z), props);
cla; dump('surf_XYZ', surf(X, Y, Z), props);
cla; dump('surf_xyZ', surf(1:3, 10:11, Z), props);
cla; dump('mesh_Z', mesh(Z), props);
cla; dump('mesh_xyZ', mesh(1:3, 10:11, Z), props);
cla; dump('pcolor_Z', pcolor(Z), props);
cla; dump('pcolor_xyZ', pcolor(1:3, 10:11, Z), props);
cla; dump('pcolor_XYZ', pcolor(X, Y, Z), props);

% A written vector puts a meshgrid surface's positions back to a vector.
cla; s = surf(X, Y, Z); s.XData = [5 6 7];
dump('surf_XYZ_written', s, {'XData', 'YData', 'XDataMode'});
% Heights of another size on counted positions count again from 1.
cla; s = surf(Z); s.ZData = magic(4);
dump('surf_Z_resized', s, {'XData', 'YData'});

pprops = {'FaceColor', 'CData', 'CDataMode', 'FaceVertexCData'};
cla; dump('p_r', patch([0 1 1], [0 0 1], 'r'), pprops);
cla; dump('p_rgb', patch([0 1 1], [0 0 1], [1 0 0]), pprops);
cla; dump('p_scalar', patch([0 1 1], [0 0 1], 5), pprops);
cla; dump('p_vert', patch([0 1 1], [0 0 1], [1; 2; 3]), pprops);
cla; dump('p_column', patch([0 1 1]', [0 0 1]', [0 0 1]'), pprops);
cla; dump('fill_r', fill([0 1 1], [0 0 1], 'r'), pprops);
close(f);

function dump(key, h, props)
for k = 1:numel(props)
    u9b_chk([key '_' props{k}], @() get(h, props{k}));
end
end
