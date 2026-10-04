% record: -noFigureWindows
% U2 of the app-building plan (ADR 0199): Units on figures, axes, panels and uicontrols, the root's
% screen values, getpixelposition, setpixelposition and movegui — on figures that are never shown.
% Nothing here prints the size of the screen it ran on: what is checked is how a position stands to
% it, so the recording holds on any display.
r = groot;
ss = get(r, 'ScreenSize');
fprintf('CHK|screen_origin|%s|exact\n', u2_text(ss(1:2)));
fprintf('CHK|screen_is_first_monitor|%d|exact\n', isequal(ss, r.MonitorPositions(1, :)));
fprintf('CHK|screen_ppi|%g|exact\n', r.ScreenPixelsPerInch);
fprintf('CHK|root_units|%s|exact\n', r.Units);
set(r, 'Units', 'normalized');
fprintf('CHK|screen_normalized|%s|exact\n', u2_text(r.ScreenSize));
set(r, 'Units', 'points');
fprintf('CHK|screen_points_ratio|%s|exact\n', u2_text(r.ScreenSize ./ [1 1 ss(3:4)]));
set(r, 'Units', 'characters');
fprintf('CHK|screen_characters_ratio|%s|exact\n', u2_text(r.ScreenSize(3:4) ./ ss(3:4)));
u2_err('root_units_bogus', @() set(r, 'Units', 'bogus'));
set(r, 'Units', 'pixels');

f = figure('Visible', 'off', 'Units', 'pixels', 'Position', [100 100 560 420], 'MenuBar', 'none', 'ToolBar', 'none');
units = {'pixels', 'points', 'inches', 'centimeters', 'characters', 'normalized'};

% --- the figure ----------------------------------------------------------------------------------
for k = 1:5
    f.Units = units{k};
    fprintf('CHK|figure_%s|%s|exact\n', units{k}, u2_text(f.Position));
    fprintf('CHK|figure_%s_same|%d|exact\n', units{k}, isequal(f.Position, f.InnerPosition) && isequal(f.Position, f.OuterPosition));
end
f.Units = 'normalized';
fprintf('CHK|figure_normalized_times_screen|%s|exact\n', u2_text(f.Position .* [ss(3:4) ss(3:4)]));
f.Units = 'points'; f.Position = [75 75 300 225]; f.Units = 'pixels';
fprintf('CHK|figure_set_points|%s|exact\n', u2_text(f.Position));
f.Units = 'characters'; f.Position = [10 10 80 20]; f.Units = 'pixels';
fprintf('CHK|figure_set_characters|%s|exact\n', u2_text(f.Position));
f.Units = 'normalized'; f.Position = [0.25 0.25 0.5 0.5]; f.Units = 'pixels';
fprintf('CHK|figure_set_normalized|%s|exact\n', u2_text((f.Position - [1 1 0 0]) ./ [ss(3:4) ss(3:4)]));
f.Position = [100 100 560 420];
u2_err('figure_units_bogus', @() set(f, 'Units', 'bogus'));
u2_chk('figure_units_upper', @() u2_setget(f, 'Units', 'PIX'));
u2_chk('figure_units_prefix', @() u2_setget(f, 'Units', 'norm'));
f.Units = 'pixels';
u2_err('figure_position_three', @() set(f, 'Position', [1 2 3]));
u2_err('figure_position_negative', @() set(f, 'Position', [1 2 -3 4]));
g = figure('Visible', 'off', 'Units', 'points', 'Position', [75 75 300 225]);
fprintf('CHK|figure_units_then_position|%s %s|exact\n', g.Units, u2_text(g.Position));
delete(g);
g = figure('Visible', 'off', 'Position', [101 101 400 300], 'Units', 'points');
fprintf('CHK|figure_position_then_units|%s %s|exact\n', g.Units, u2_text(g.Position));
delete(g);

% --- axes: a plot box pinned in each unit ----------------------------------------------------------
a = axes(f, 'Position', [0.1 0.2 0.5 0.6]);
fprintf('CHK|axes_default_units|%s|exact\n', a.Units);
for k = 1:6
    a.Units = units{k};
    fprintf('CHK|axes_%s|%s|exact\n', units{k}, u2_text(a.Position));
    fprintf('CHK|axes_%s_inner|%d|exact\n', units{k}, isequal(a.Position, a.InnerPosition));
end
a.Units = 'pixels'; a.Position = [50 60 300 200];
fprintf('CHK|axes_pixels_set|%s|exact\n', u2_text(a.Position));
fprintf('CHK|axes_pixels_constraint|%s|exact\n', a.PositionConstraint);
f.Position = [100 100 700 500];
fprintf('CHK|axes_pixels_held_on_resize|%s|exact\n', u2_text(a.Position));
a.Units = 'normalized';
fprintf('CHK|axes_back_to_normalized|%s|exact\n', u2_text(a.Position));
f.Position = [100 100 560 420];
fprintf('CHK|axes_normalized_held_on_resize|%s|exact\n', u2_text(a.Position));
fprintf('CHK|axes_getpixelposition|%s|exact\n', u2_text(getpixelposition(a)));
u2_err('axes_units_bogus', @() set(a, 'Units', 'bogus'));
u2_chk('axes_units_prefix', @() u2_setget(a, 'Units', 'char'));
a.Units = 'normalized';
a2 = axes(f, 'Units', 'pixels', 'Position', [10 10 100 80]);
fprintf('CHK|axes_units_then_position|%s %s|exact\n', a2.Units, u2_text(a2.Position));
a3 = axes(f, 'Position', [0.25 0.25 0.5 0.5], 'Units', 'pixels');
fprintf('CHK|axes_position_then_units|%s %s|exact\n', a3.Units, u2_text(a3.Position));
a4 = axes(f, 'OuterPosition', [0 0 0.5 0.5]);
a4.Units = 'pixels';
fprintf('CHK|axes_outer_pixels|%s|exact\n', u2_text(a4.OuterPosition));
delete([a a2 a3 a4]);

% --- a uicontrol and a panel in each unit -----------------------------------------------------------
c = uicontrol(f, 'Position', [20 30 100 40]);
p = uipanel(f, 'Units', 'pixels', 'Position', [50 60 200 150]);
for k = 1:6
    c.Units = units{k}; p.Units = units{k};
    fprintf('CHK|uicontrol_%s|%s|exact\n', units{k}, u2_text(c.Position));
    fprintf('CHK|panel_%s|%s|exact\n', units{k}, u2_text(p.Position));
    fprintf('CHK|panel_inner_%s|%s|exact\n', units{k}, u2_text(p.InnerPosition));
end
c.Units = 'normalized'; c.Position = [0.5 0.5 0.25 0.25]; c.Units = 'pixels';
fprintf('CHK|uicontrol_set_normalized|%s|exact\n', u2_text(c.Position));
c.Units = 'normalized';
f.Position = [100 100 700 500];
fprintf('CHK|uicontrol_normalized_after_resize|%s|exact\n', u2_text(c.Position));
fprintf('CHK|uicontrol_pixels_after_resize|%s|exact\n', u2_text(getpixelposition(c)));
f.Position = [100 100 560 420];
p.Units = 'pixels';
k1 = uicontrol(p, 'Units', 'normalized', 'Position', [0.5 0.5 0.25 0.25]);
fprintf('CHK|child_in_panel|%s|exact\n', u2_text(getpixelposition(k1)));
fprintf('CHK|child_in_panel_in_figure|%s|exact\n', u2_text(getpixelposition(k1, true)));
k1.Units = 'characters';
fprintf('CHK|child_in_panel_characters|%s|exact\n', u2_text(k1.Position));
k1.Units = 'points';
fprintf('CHK|child_in_panel_points|%s|exact\n', u2_text(k1.Position));
u1 = uicontrol(f, 'Units', 'normalized');
fprintf('CHK|uicontrol_default_in_normalized|%s|exact\n', u2_text(u1.Position));
u2 = uicontrol(f, 'Units', 'normalized', 'Position', [0.1 0.1 0.2 0.2]);
u2.Units = 'pixels';
fprintf('CHK|uicontrol_units_then_position|%s|exact\n', u2_text(u2.Position));
u3 = uicontrol(f, 'Position', [57 43 112 84], 'Units', 'normalized');
fprintf('CHK|uicontrol_position_then_units|%s|exact\n', u2_text(u3.Position));
u4 = uicontrol(f, 'FontUnits', 'normalized', 'FontSize', 0.5, 'Position', [10 10 100 40]);
u4.FontUnits = 'pixels';
fprintf('CHK|fontunits_normalized_to_pixels|%s|exact\n', u2_text(u4.FontSize));
u2_err('uicontrol_units_bogus', @() set(c, 'Units', 'bogus'));

% --- getpixelposition and setpixelposition ---------------------------------------------------------
fprintf('CHK|gpp_figure|%s|exact\n', u2_text(getpixelposition(f)));
fprintf('CHK|gpp_figure_recursive|%s|exact\n', u2_text(getpixelposition(f, true)));
fprintf('CHK|gpp_root|%s|exact\n', u2_text(getpixelposition(0)));
u2_chk('gpp_no_arguments', @() getpixelposition());
u2_chk('gpp_not_a_handle', @() getpixelposition(5.5));
u2_chk('gpp_two_handles', @() getpixelposition([c p]));
u2_chk('gpp_three_arguments', @() getpixelposition(c, 1, 2));
c.Units = 'normalized';
setpixelposition(c, [5 6 70 80]);
fprintf('CHK|spp_keeps_units|%s|exact\n', c.Units);
fprintf('CHK|spp_position|%s|exact\n', u2_text(c.Position));
fprintf('CHK|spp_pixels|%s|exact\n', u2_text(getpixelposition(c)));
setpixelposition(k1, [40 50 30 20], true);
fprintf('CHK|spp_recursive|%s|exact\n', u2_text(getpixelposition(k1)));
fprintf('CHK|spp_recursive_in_figure|%s|exact\n', u2_text(getpixelposition(k1, true)));
fprintf('CHK|spp_recursive_units|%s|exact\n', k1.Units);
u2_err('spp_one_argument', @() setpixelposition(c));
u2_err('spp_three_numbers', @() setpixelposition(c, [1 2 3]));
u2_err('spp_text', @() setpixelposition(c, 'abcd'));
u2_err('spp_not_a_handle', @() setpixelposition(5.5, [1 2 3 4]));
u2_err('spp_negative', @() setpixelposition(c, [1 2 -3 4]));
setpixelposition(f, [10 20 300 200]);
fprintf('CHK|spp_figure|%s|exact\n', u2_text(getpixelposition(f)));
a = axes(f);
setpixelposition(a, [30 40 200 100]);
fprintf('CHK|spp_axes|%s|exact\n', u2_text(getpixelposition(a)));
fprintf('CHK|spp_axes_units|%s|exact\n', a.Units);

% --- movegui: where each word puts a 300 by 200 figure, measured from the screen's edges ------------
ss = get(0, 'ScreenSize');
words = {'north', 'south', 'east', 'west', 'northeast', 'northwest', 'southeast', 'southwest', 'center'};
for k = 1:numel(words)
    f.Position = [200 200 300 200];
    movegui(f, words{k});
    pos = f.Position;
    gaps = [pos(1) - ss(1), pos(2) - ss(2), ss(3) - (pos(1) + pos(3) - 1), ss(4) - (pos(2) + pos(4) - 1)];
    switch words{k}
        case 'north', shown = [gaps(1) - gaps(3), gaps(4)];
        case 'south', shown = [gaps(1) - gaps(3), gaps(2)];
        case 'east', shown = [gaps(3), gaps(2) - gaps(4)];
        case 'west', shown = [gaps(1), gaps(2) - gaps(4)];
        case 'northeast', shown = [gaps(3), gaps(4)];
        case 'northwest', shown = [gaps(1), gaps(4)];
        case 'southeast', shown = [gaps(3), gaps(2)];
        case 'southwest', shown = [gaps(1), gaps(2)];
        otherwise, shown = [gaps(1) - gaps(3), gaps(2) - gaps(4)];
    end
    fprintf('CHK|movegui_%s|%s|exact\n', words{k}, u2_text(shown));
    fprintf('CHK|movegui_%s_size|%s|exact\n', words{k}, u2_text(pos(3:4)));
end
f.Position = [200 200 300 200]; movegui(f, 'onscreen');
fprintf('CHK|movegui_onscreen_already|%s|exact\n', u2_text(f.Position));
f.Position = [200 200 300 200]; movegui(f, [10 20]);
fprintf('CHK|movegui_offset|%s|exact\n', u2_text(f.Position));
f.Position = [200 200 300 200]; movegui(f, [-10 -20]); pos = f.Position;
fprintf('CHK|movegui_negative_offset|%s|exact\n', u2_text([ss(3) - (pos(1) + pos(3) - 1), ss(4) - (pos(2) + pos(4) - 1)]));
f.Position = [-500 -500 300 200]; movegui(f);
fprintf('CHK|movegui_default_is_onscreen|%s|exact\n', u2_text(f.Position));
f.Position = [ss(3) + 100, ss(4) + 100, 300, 200]; movegui(f, 'onscreen'); pos = f.Position;
fprintf('CHK|movegui_onscreen_from_beyond|%s|exact\n', u2_text([ss(3) - (pos(1) + pos(3) - 1), ss(4) - (pos(2) + pos(4) - 1)]));
f.Position = [200 200 300 200]; movegui(c, 'southwest');
fprintf('CHK|movegui_by_child|%s|exact\n', u2_text(f.Position));
f.Units = 'normalized'; movegui(f, 'southwest');
fprintf('CHK|movegui_keeps_units|%s|exact\n', f.Units);
f.Units = 'pixels';
fprintf('CHK|movegui_normalized_figure|%s|exact\n', u2_text(f.Position));
u2_err('movegui_bogus', @() movegui(f, 'bogus'));
u2_err('movegui_not_a_handle', @() movegui(5.5));
u2_err('movegui_three_numbers', @() movegui(f, [1 2 3]));
u2_err('movegui_cell', @() movegui(f, {1}));
uf = uifigure('Visible', 'off', 'Position', [200 200 300 200]);
movegui(uf, 'northeast'); pos = uf.Position;
fprintf('CHK|movegui_uifigure|%s|exact\n', u2_text([ss(3) - (pos(1) + pos(3) - 1), ss(4) - (pos(2) + pos(4) - 1)]));
delete(uf);
delete(f);
