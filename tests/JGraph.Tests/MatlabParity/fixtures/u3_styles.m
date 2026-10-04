% record: -noFigureWindows
% U3 of the app-building plan (ADR 0200): the ten uicontrol styles in a figure never shown — the
% names a uicontrol lists and the hidden ones it answers, each style's defaults, and what Value,
% Min, Max, SliderStep, ListboxTop, String and CData take and refuse.
styles = {'pushbutton', 'togglebutton', 'radiobutton', 'checkbox', 'edit', 'text', 'slider', 'frame', 'listbox', 'popupmenu'};
f = figure('Visible', 'off', 'Position', [100 100 560 420]);

% --- the names --------------------------------------------------------------------------------
c = uicontrol(f);
fprintf('CHK|get_names|%s|exact\n', strjoin(sort(fieldnames(get(c)))', ' '));
fprintf('CHK|set_names|%s|exact\n', strjoin(sort(fieldnames(set(c)))', ' '));
for name = {'TooltipString', 'Selected', 'SelectionHighlight', 'HitTest', 'TooltipStr', ...
        'PickableParts', 'Clipping', 'FontColor', 'Text', 'Layout', 'ResizeFcn', 'SizeChangedFcn'}
    u2_chk(['hidden_' name{1}], @() get(c, name{1}));
end
u2_chk('hidden_UIContextMenu_empty', @() isempty(get(c, 'UIContextMenu')));
for name = {'Selected', 'SelectionHighlight', 'HitTest'}
    values = {'on', 'off', true, 0, 'bogus', 5};
    for k = 1:numel(values)
        u2_chk(sprintf('set_%s_%d', name{1}, k), @() u2_setget(c, name{1}, values{k}));
    end
end
u2_chk('set_Extent', @() u2_setget(c, 'Extent', [0 0 1 1]));
u2_chk('set_Type', @() u2_setget(c, 'Type', 'x'));
u2_chk('set_BeingDeleted', @() u2_setget(c, 'BeingDeleted', 'on'));
u2_chk('set_Enable_words', @() set(c, 'Enable'));
u2_chk('set_Style_words', @() set(c, 'Style'));
enables = {'INACTIVE', 'ina', true, 0, 'o', 'off', 'on'};
for k = 1:numel(enables)
    u2_chk(sprintf('set_Enable_%d', k), @() u2_setget(c, 'Enable', enables{k}));
end
u2_chk('set_ButtonDownFcn_char', @() u2_setget(c, 'ButtonDownFcn', 'disp(1)'));
u2_chk('set_ButtonDownFcn_number', @() u2_setget(c, 'ButtonDownFcn', 5));
u2_chk('set_HorizontalAlignment_r', @() u2_setget(c, 'HorizontalAlignment', 'r'));
delete(c);

% --- defaults per style -----------------------------------------------------------------------
props = {'Value', 'Min', 'Max', 'SliderStep', 'ListboxTop', 'HorizontalAlignment', 'BackgroundColor', ...
    'ForegroundColor', 'Position', 'String', 'Enable', 'CData', 'Units', 'FontName', 'FontSize'};
for s = 1:numel(styles)
    c = uicontrol(f, 'Style', styles{s});
    for k = 1:numel(props)
        fprintf('CHK|default_%s_%s|%s|exact\n', styles{s}, props{k}, u2_text(get(c, props{k})));
    end
    delete(c);
end

% --- Value: the same for every style ----------------------------------------------------------
values = {0, 1, 0.5, 5, -1, [1 2], [2; 3], [], true, int8(1), single(0.25), NaN, Inf, 'a', {1}, 1 + 2i, [1 2; 3 4], zeros(1, 0)};
for s = {'pushbutton', 'checkbox', 'slider', 'listbox', 'popupmenu', 'edit'}
    c = uicontrol(f, 'Style', s{1}, 'String', {'one', 'two', 'three'});
    for k = 1:numel(values)
        u2_chk(sprintf('value_%s_%d', s{1}, k), @() u2_setget(c, 'Value', values{k}));
    end
    delete(c);
end

c = uicontrol(f);
u2_err('value_string', @() u3_assign(c, "1"));
delete(c);

% --- Value through a change of style ----------------------------------------------------------
c = uicontrol(f, 'Style', 'listbox', 'String', {'a', 'b', 'c'});
fprintf('CHK|listbox_value_unset|%s|exact\n', u2_text(c.Value));
c.Style = 'checkbox'; fprintf('CHK|listbox_to_checkbox|%s|exact\n', u2_text(c.Value));
c.Style = 'popupmenu'; fprintf('CHK|checkbox_to_popupmenu|%s|exact\n', u2_text(c.Value));
c.Value = 3; c.String = {'a'}; fprintf('CHK|popup_value_past_items|%s|exact\n', u2_text(c.Value));
c.Style = 'pushbutton'; fprintf('CHK|popup_to_pushbutton|%s|exact\n', u2_text(c.Value));
delete(c);
c = uicontrol(f, 'Style', 'checkbox', 'Value', 1); c.Style = 'listbox';
fprintf('CHK|checked_to_listbox|%s|exact\n', u2_text(c.Value)); delete(c);
c = uicontrol(f, 'Style', 'togglebutton', 'Min', 2, 'Max', 7);
fprintf('CHK|toggle_min_max_value|%s|exact\n', u2_text(c.Value)); delete(c);
c = uicontrol(f, 'Style', 'slider', 'Min', 2, 'Max', 7);
fprintf('CHK|slider_min_max_value|%s|exact\n', u2_text(c.Value)); delete(c);

% --- Min, Max, SliderStep, ListboxTop ---------------------------------------------------------
c = uicontrol(f, 'Style', 'slider');
sets = {
    'Max', {10, 0, -5, [1 2], 'a', [], true, int8(3), NaN, Inf}
    'Min', {-10, 20, [1 2], 'a', [], NaN, -Inf}
    'SliderStep', {[0.2 0.5], [0 1], [-1 1], [1 2 3], 'a', [2 3], 0.5, [0.1; 0.2], [NaN 1], [Inf 1], true, [1 1], {0.1, 0.2}}
    'ListboxTop', {0, 2, 1.5, -1, 'a', [1 2], [], 100, NaN, int8(2)}
    };
for r = 1:size(sets, 1)
    for k = 1:numel(sets{r, 2})
        u2_chk(sprintf('set_%s_%d', sets{r, 1}, k), @() u2_setget(c, sets{r, 1}, sets{r, 2}{k}));
    end
end
lastwarn('', '');
warning('off', 'MATLAB:hg:UIControlSliderStepValueDifference');
c.SliderStep = [0.5 0.1]; [msg, id] = lastwarn;
warning('on', 'MATLAB:hg:UIControlSliderStepValueDifference');
fprintf('CHK|warn_sliderstep|%s : %s|exact\n', id, u2_text(msg));
fprintf('CHK|sliderstep_kept|%s|exact\n', u2_text(c.SliderStep));
delete(c);

% --- String: what a list reads, and newlines --------------------------------------------------
forms = {{'a', 'b', 'c'}, 'a|b|c', ['ab'; 'cd'], '', {}, {'a'; 'b'}, sprintf('l1\nl2'), ["x" "y"], {'a', 5}, 5, [1 2 3], {''}, ...
    "p|q", 'ab|c|', '|', 'a||b', sprintf('a|b\nc'), sprintf('a\n\nb'), sprintf('ab\n'), {sprintf('a\nb'), 'c'}, {'a|b', 'c'}};
for s = {'listbox', 'popupmenu', 'edit', 'text', 'pushbutton'}
    c = uicontrol(f, 'Style', s{1});
    for k = 1:numel(forms)
        u2_chk(sprintf('string_%s_%d', s{1}, k), @() u2_setget(c, 'String', forms{k}));
    end
    delete(c);
end
a = uicontrol(f, 'String', 'x|y|z', 'Style', 'popupmenu'); fprintf('CHK|string_then_style|%s|exact\n', u2_text(a.String));
a.Style = 'text'; fprintf('CHK|list_then_text|%s|exact\n', u2_text(a.String));
d = uicontrol(f, 'Style', 'text', 'String', 'x|y|z'); fprintf('CHK|text_with_bars|%s|exact\n', u2_text(d.String));
d.Style = 'listbox'; fprintf('CHK|text_then_listbox|%s|exact\n', u2_text(d.String));
delete(a); delete(d);

% --- a multi-line edit field ------------------------------------------------------------------
c = uicontrol(f, 'Style', 'edit', 'Max', 2, 'String', {'line one', 'line two'});
fprintf('CHK|multiline_string|%s|exact\n', u2_text(c.String));
fprintf('CHK|multiline_value|%s|exact\n', u2_text(c.Value));
c.String = ['ab '; 'cde']; c.Max = 1;
fprintf('CHK|multiline_back_to_one|%s|exact\n', u2_text(c.String));
delete(c);

% --- CData ------------------------------------------------------------------------------------
c = uicontrol(f);
cds = {zeros(4, 4, 3), zeros(4, 4), ones(2, 3, 3) * 2, uint8(zeros(4, 4, 3)), 'a', [], zeros(4, 4, 4), single(zeros(2, 2, 3)), ...
    logical(ones(2, 2, 3)), {1}, NaN(2, 2, 3), int16(zeros(2, 2, 3)), uint16(zeros(2, 2, 3)), -ones(2, 2, 3), zeros(1, 1, 3), 0.5};
for k = 1:numel(cds)
    try
        set(c, 'CData', cds{k});
        v = get(c, 'CData');
        fprintf('CHK|cdata_%d|%s %s|exact\n', k, class(v), mat2str(size(v)));
    catch err
        fprintf('CHK|cdata_%d|%s|exact\n', k, err.identifier);
        fprintf('CHK|cdata_%d_msg|%s|exact\n', k, u2_text(err.message));
    end
end
delete(c);

% --- the older spellings ----------------------------------------------------------------------
c = uicontrol(f);
c.Tooltip = 'tip'; fprintf('CHK|tooltip_to_tooltipstring|%s|exact\n', u2_text(get(c, 'TooltipString')));
set(c, 'TooltipString', 'old'); fprintf('CHK|tooltipstring_to_tooltip|%s|exact\n', u2_text(c.Tooltip));
set(c, 'TooltipStr', 'older'); fprintf('CHK|tooltipstr_to_tooltip|%s|exact\n', u2_text(c.Tooltip));
u2_chk('tooltipstring_number', @() u2_setget(c, 'TooltipString', 5));
u2_chk('tooltipstring_cell', @() u2_setget(c, 'TooltipString', {'a', 'b'}));
u2_chk('tooltip_string', @() u2_setget(c, 'Tooltip', "s"));
u2_chk('tooltip_string_array', @() u2_setget(c, 'Tooltip', ["s" "t"]));
u2_chk('tooltip_char_matrix', @() u2_setget(c, 'Tooltip', ['ab'; 'cd']));
u2_chk('tooltip_empty', @() u2_setget(c, 'Tooltip', []));
m = uicontextmenu(f);
u2_chk('contextmenu_menu', @() isequal(u2_setget(c, 'ContextMenu', m), m));
u2_chk('uicontextmenu_reads', @() isequal(get(c, 'UIContextMenu'), m));
u2_chk('uicontextmenu_empty', @() isempty(u2_setget(c, 'UIContextMenu', [])));
u2_chk('contextmenu_after', @() isempty(get(c, 'ContextMenu')));
u2_chk('uicontextmenu_menu', @() isequal(u2_setget(c, 'UIContextMenu', m), m));
u2_chk('contextmenu_number', @() u2_setget(c, 'ContextMenu', 5.5));
u2_chk('contextmenu_figure', @() u2_setget(c, 'ContextMenu', f));
u2_chk('contextmenu_uicontrol', @() u2_setget(c, 'ContextMenu', c));
u2_chk('contextmenu_empty_text', @() isempty(u2_setget(c, 'ContextMenu', '')));
c.ContextMenu = m; delete(m);
u2_chk('contextmenu_after_delete', @() isempty(get(c, 'ContextMenu')));
delete(c);

% --- a frame holds nothing --------------------------------------------------------------------
fr = uicontrol(f, 'Style', 'frame', 'Position', [10 10 100 100]);
inside = uicontrol(f, 'Style', 'text', 'String', 'in', 'Position', [20 20 50 20]);
fprintf('CHK|frame_children|%d|exact\n', numel(fr.Children));
fprintf('CHK|frame_neighbour_parent|%d|exact\n', inside.Parent == f);
u2_chk('frame_as_named_parent', @() uicontrol('Parent', fr));
u2_chk('frame_focus_form', @() uicontrol(fr) == fr);
delete(f);
