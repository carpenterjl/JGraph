% record: -noFigureWindows
% U2 of the app-building plan (ADR 0199): uipanel in a classic figure and in a uifigure, neither
% ever shown — its defaults, R2025b's coercions and refusals, where its inner area lies, nesting,
% and the pixel positions of what is placed in it.
f = figure('Visible', 'off', 'Position', [100 100 560 420]);
uf = uifigure('Visible', 'off', 'Position', [100 100 560 420]);

% --- defaults, in both kinds of figure -----------------------------------------------------------
props = {'Type', 'Title', 'TitlePosition', 'BorderType', 'BorderWidth', 'BackgroundColor', 'ForegroundColor', ...
    'BorderColor', 'HighlightColor', 'FontName', 'FontSize', 'FontUnits', 'FontWeight', 'FontAngle', 'Units', ...
    'Position', 'InnerPosition', 'OuterPosition', 'Visible', 'Enable', 'Scrollable', 'AutoResizeChildren', ...
    'Clipping', 'Tag', 'Tooltip', 'HandleVisibility', 'SizeChangedFcn', 'ButtonDownFcn', 'CreateFcn', 'DeleteFcn', ...
    'BusyAction', 'Interruptible', 'BeingDeleted'};
p = uipanel(f);
q = uipanel(uf);
for k = 1:numel(props)
    fprintf('CHK|classic_default_%s|%s|exact\n', props{k}, u2_text(get(p, props{k})));
    fprintf('CHK|uifigure_default_%s|%s|exact\n', props{k}, u2_text(get(q, props{k})));
end
fprintf('CHK|default_parent|%d %d|exact\n', get(p, 'Parent') == f, get(q, 'Parent') == uf);
fprintf('CHK|default_children|%d|exact\n', numel(get(p, 'Children')));
for name = {'ShadowColor', 'ResizeFcn', 'TooltipString', 'PickableParts', 'Extent', 'Selected'}
    u2_chk(['hidden_' name{1}], @() get(p, name{1}));
end
delete(p); delete(q);

% --- the forms of the call -----------------------------------------------------------------------
c = uicontrol(f);
ax = axes(f);
pp = uipanel(f);
u2_chk('form_parent', @() get(uipanel(f), 'Parent') == f);
u2_chk('form_parent_pairs', @() get(uipanel(f, 'Title', 'T'), 'Title'));
u2_chk('form_named_parent', @() get(uipanel('Parent', f), 'Parent') == f);
u2_chk('form_struct', @() get(uipanel(f, struct('Title', 'S2')), 'Title'));
u2_chk('form_odd', @() uipanel('Title'));
u2_chk('form_parent_odd', @() uipanel(f, 'Title'));
u2_chk('form_not_a_handle', @() uipanel(5.5));
u2_chk('form_unknown', @() uipanel(f, 'Bogus', 1));
u2_chk('form_bad_word', @() uipanel(f, 'BorderType', 'bogus'));
u2_chk('form_in_uicontrol', @() uipanel(c));
u2_chk('form_in_uicontrol_pairs', @() uipanel(c, 'Title', 'x'));
u2_chk('form_in_axes', @() uipanel(ax));
u2_chk('form_named_axes', @() uipanel('Parent', ax));
u2_chk('form_in_panel', @() get(uipanel(pp), 'Parent') == pp);
u2_chk('form_uicontrol_in_panel', @() get(uicontrol(pp), 'Parent') == pp);
u2_chk('form_uicontrol_named_panel', @() get(uicontrol('Parent', pp), 'Parent') == pp);
u2_chk('form_axes_in_panel', @() get(axes(pp), 'Parent') == pp);
u2_chk('form_axes_named_panel', @() get(axes('Parent', pp), 'Parent') == pp);
u2_chk('form_in_uifigure', @() get(uipanel(uf), 'Units'));
u2_chk('form_numbers', @() uipanel(f, 5, 6));
u2_chk('form_bad_title', @() uipanel(f, 'Title', true));
u2_chk('form_bad_width', @() uipanel(f, 'BorderWidth', -1));
u2_chk('form_bad_position', @() uipanel(f, 'Position', [1 2 3]));
clf(f);

% --- what each property takes and refuses --------------------------------------------------------
p = uipanel(f, 'Units', 'pixels', 'Position', [50 60 200 150]);
sets = {
    'Title', {'abc', "str", 5, {'a', 'b'}, ['ab'; 'cd'], '', [], true, ["a" "b"]}
    'BorderType', {'none', 'line', 'LINE', 'n', 'etched', 'bogus', 5}
    'BorderWidth', {2, 0, -1, 1.5, 'a', [1 2], [], int8(3)}
    'TitlePosition', {'centertop', 'rightbottom', 'CENTERTOP', 'left', 'c', 'bogus'}
    'BackgroundColor', {[1 0 0], 'r', '#00FF00', 'none', [2 0 0], [1 0], 'bogus', uint8([255 0 0])}
    'ForegroundColor', {[0 0 1], 'none', 'g'}
    'HighlightColor', {[0 1 1], 'none'}
    'BorderColor', {[0 1 0], 'k', 'none'}
    'FontSize', {10, 0, -1, 'a', [1 2], 8.5}
    'FontName', {'Arial', '', 5, "Courier"}
    'FontWeight', {'bold', 'light', 'demi', 'normal', 'b', 'heavy'}
    'FontAngle', {'italic', 'oblique', 'normal', 'bogus'}
    'Units', {'normalized', 'inches', 'centimeters', 'points', 'characters', 'PIX', 'bogus'}
    'Position', {[10 20 30 40], [10 20 0 40], [10 20 -5 40], [1 2 3], 'a', [10 20 30 NaN], {1}}
    'InnerPosition', {[10 20 30 40]}
    'OuterPosition', {[11 21 31 41]}
    'Visible', {'off', 'on', true, false, 1, 0, 'bogus', "off"}
    'Enable', {'off', 'inactive', 'on', 'bogus'}
    'Scrollable', {'on', 'off', true}
    'AutoResizeChildren', {'on', 'off', 'bogus'}
    'Clipping', {'off', 'on'}
    'Tooltip', {'tip', {'a', 'b'}, 5}
    'HandleVisibility', {'off', 'callback', 'CALL', 'on', 'bogus'}
    'SizeChangedFcn', {'disp(1)', {@disp, 1}, '', [], 5}
    'ResizeFcn', {'disp(2)'}
    'Layout', {1}
    'Type', {'x'}
    };
for s = 1:size(sets, 1)
    name = sets{s, 1};
    for k = 1:numel(sets{s, 2})
        u2_chk(sprintf('set_%s_%d', name, k), @() u2_setget(p, name, sets{s, 2}{k}));
    end
    set(p, 'Units', 'pixels');
end
fprintf('CHK|resizefcn_is_sizechangedfcn|%s|exact\n', u2_text(get(p, 'SizeChangedFcn')));
p.SizeChangedFcn = '';
p.FontUnits = 'points'; p.FontSize = 12;
p.FontUnits = 'pixels'; fprintf('CHK|fontunits_pixels|%s|exact\n', u2_text(p.FontSize));
p.FontUnits = 'inches'; fprintf('CHK|fontunits_inches|%s|exact\n', u2_text(p.FontSize));
p.FontUnits = 'points'; fprintf('CHK|fontunits_points|%s|exact\n', u2_text(p.FontSize));
u2_chk('get_unknown', @() get(p, 'Bogus'));
u2_err('set_unknown', @() set(p, 'Bogus', 1));

% --- warnings -----------------------------------------------------------------------------------
lastwarn('', '');
p.BorderType = 'etchedin'; [msg, id] = lastwarn;
fprintf('CHK|warn_bordertype|%s : %s|exact\n', id, u2_text(msg));
fprintf('CHK|bordertype_kept|%s|exact\n', p.BorderType);
lastwarn('', '');
p.ShadowColor = [0 0 0]; [msg, id] = lastwarn;
fprintf('CHK|warn_shadowcolor|%s : %s|exact\n', id, u2_text(msg));
lastwarn('', '');
p.AutoResizeChildren = 'on'; p.SizeChangedFcn = 'disp(1)'; [msg, id] = lastwarn;
fprintf('CHK|warn_sizechanged_panel|%s : %s|exact\n', id, u2_text(msg));
p.SizeChangedFcn = ''; p.AutoResizeChildren = 'off';
lastwarn('', '');
p.SizeChangedFcn = 'disp(1)'; p.AutoResizeChildren = 'on'; [msg, id] = lastwarn;
fprintf('CHK|warn_autoresize_panel|%s : %s|exact\n', id, u2_text(msg));
delete(p);

% --- where the inner area lies: a normalized child filling it, read back in pixels -----------------
cases = {
    {}
    {'Title', 'T'}
    {'Title', 'T', 'FontSize', 12}
    {'Title', 'T', 'FontSize', 20}
    {'Title', 'T', 'FontUnits', 'pixels', 'FontSize', 20}
    {'Title', 'Tg', 'FontName', 'Arial', 'FontSize', 10}
    {'Title', 'T', 'TitlePosition', 'centertop'}
    {'Title', 'T', 'TitlePosition', 'leftbottom'}
    {'Title', 'T', 'TitlePosition', 'rightbottom'}
    {'BorderType', 'none'}
    {'BorderType', 'none', 'Title', 'T'}
    {'BorderType', 'etchedin'}
    {'BorderType', 'beveledout'}
    {'BorderWidth', 3}
    {'BorderWidth', 0}
    {'BorderWidth', 3, 'Title', 'T'}
    {'BorderType', 'etchedin', 'BorderWidth', 3}
    {'BorderType', 'beveledin', 'BorderWidth', 3}
    {'BorderType', 'etchedin', 'Title', 'T'}
    {'Title', {'two'}}
    };
warning('off', 'MATLAB:Uipanel:UnsupportedBorderType');
for k = 1:numel(cases)
    for kind = 1:2
        if kind == 1
            p = uipanel(f, 'Units', 'pixels', 'Position', [50 60 200 150], cases{k}{:});
            word = 'classic';
        else
            p = uipanel(uf, 'Position', [50 60 200 150], cases{k}{:});
            word = 'uifigure';
        end
        c = uicontrol(p, 'Style', 'text', 'Units', 'normalized', 'Position', [0 0 1 1]);
        c.Units = 'pixels';
        fprintf('CHK|inner_%s_%d_child|%s|exact\n', word, k, u2_text(c.Position));
        fprintf('CHK|inner_%s_%d_inner|%s|exact\n', word, k, u2_text(p.InnerPosition));
        fprintf('CHK|inner_%s_%d_outer|%s|exact\n', word, k, u2_text(p.OuterPosition));
        fprintf('CHK|inner_%s_%d_child_in_figure|%s|exact\n', word, k, u2_text(getpixelposition(c, true)));
        delete(p);
    end
end
warning('on', 'MATLAB:Uipanel:UnsupportedBorderType');

% --- nesting, and an axes in a panel ---------------------------------------------------------------
p = uipanel(f, 'Units', 'pixels', 'Position', [50 60 300 250], 'Title', 'Outer', 'Tag', 'p');
p2 = uipanel(p, 'Units', 'pixels', 'Position', [20 30 200 150], 'Tag', 'p2');
b = uicontrol(p2, 'Position', [10 10 50 20], 'Tag', 'b');
fprintf('CHK|nested_panel|%s|exact\n', u2_text(getpixelposition(p2)));
fprintf('CHK|nested_panel_in_figure|%s|exact\n', u2_text(getpixelposition(p2, true)));
fprintf('CHK|nested_control|%s|exact\n', u2_text(getpixelposition(b)));
fprintf('CHK|nested_control_in_figure|%s|exact\n', u2_text(getpixelposition(b, true)));
a = axes(p2, 'Position', [0.2 0.25 0.5 0.5], 'Tag', 'a');
fprintf('CHK|axes_in_panel_units|%s|exact\n', a.Units);
fprintf('CHK|axes_in_panel_position|%s|exact\n', u2_text(a.Position));
fprintf('CHK|axes_in_panel_pixels|%s|exact\n', u2_text(getpixelposition(a)));
fprintf('CHK|axes_in_panel_in_figure|%s|exact\n', u2_text(getpixelposition(a, true)));
fprintf('CHK|ancestor_figure|%d|exact\n', ancestor(b, 'figure') == f);
fprintf('CHK|ancestor_panel|%d|exact\n', ancestor(b, 'uipanel') == p2);
fprintf('CHK|ancestor_toplevel|%d|exact\n', ancestor(a, 'uipanel', 'toplevel') == p);
fprintf('CHK|ancestor_self|%d|exact\n', ancestor(p2, 'uipanel') == p2);
fprintf('CHK|panel_children|%s|exact\n', u2_tags(p.Children));
fprintf('CHK|inner_panel_children|%s|exact\n', u2_tags(p2.Children));
p2.Visible = 'off';
fprintf('CHK|hidden_panel_children_visible|%s %s|exact\n', char(b.Visible), char(a.Visible));
p2.Visible = 'on';
p2.Enable = 'off';
fprintf('CHK|disabled_panel_child_enable|%s|exact\n', char(b.Enable));
delete(f); delete(uf);
