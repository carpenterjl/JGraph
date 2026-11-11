% record: -noFigureWindows
% A figure nothing has placed answers a corner on the screen, not the origin: across the middle of
% the primary screen with its top about 202 pixels below the screen's. The figure's own size is the
% engine's (R2025b sizes a classic figure by the screen, JGraph 640 by 480), so what is checked is
% how the rectangle stands to the screen, which holds on any display.
set(groot, 'Units', 'pixels');
ss = get(groot, 'ScreenSize');
f = figure(1);
p = get(gcf, 'Position');
fprintf('CHK|figure_not_origin|%d|exact\n', p(1) > 1 && p(2) > 1);
fprintf('CHK|figure_on_screen|%d|exact\n', p(1) + p(3) <= ss(3) && p(2) + p(4) <= ss(4));
fprintf('CHK|figure_centred|%g|abs=1\n', p(1) - (ss(3) - p(3)) / 2);
fprintf('CHK|figure_top_gap|%g|abs=1\n', ss(4) - p(2) - p(4));
fprintf('CHK|figure_position_is_inner|%d|exact\n', isequal(f.Position, f.InnerPosition));

% Changing the size alone keeps the corner.
corner = f.Position(1:2);
f.Position(3) = 300;
fprintf('CHK|figure_corner_kept|%d|exact\n', isequal(f.Position(1:2), corner));
fprintf('CHK|figure_width_set|%g|exact\n', f.Position(3));

% In normalized units the corner is the same fraction of the screen.
g = figure(2);
q = g.Position;
g.Units = 'normalized';
n = g.Position;
fprintf('CHK|figure_normalized_corner|%d|exact\n', all(abs(n(1:2) .* ss(3:4) - (q(1:2) - 1)) <= 1));
fprintf('CHK|figure_normalized_inside|%d|exact\n', all(n > 0 & n < 1));

% A uifigure is 560 by 420 in both engines, so its corner is checked exactly.
u = uifigure('Visible', 'off');
r = u.Position;
fprintf('CHK|uifigure_size|%s|exact\n', mat2str(r(3:4)));
fprintf('CHK|uifigure_corner_x|%g|exact\n', r(1) - (ss(3) - r(3)) / 2);
fprintf('CHK|uifigure_top_gap|%g|exact\n', ss(4) - r(2) - r(4));
close all force
delete(u);
