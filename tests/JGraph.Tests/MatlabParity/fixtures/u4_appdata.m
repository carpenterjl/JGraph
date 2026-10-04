% record: -noFigureWindows
% U4 of the app-building plan (ADR 0201): guihandles, guidata and the application-data verbs —
% what they store, where, and R2025b's refusals. guidata is one of the figure's application data.
f = figure('Visible', 'off', 'Tag', 'mainFig', 'MenuBar', 'none', 'ToolBar', 'none');
p = uipanel(f, 'Tag', 'panel1');
a = uicontrol(f, 'Style', 'edit', 'Tag', 'nameEdit');
b = uicontrol(p, 'Style', 'pushbutton', 'Tag', 'go');
b2 = uicontrol(f, 'Style', 'pushbutton', 'Tag', 'go');
uicontrol(f, 'Style', 'text', 'Tag', 'not a name');
uicontrol(f, 'Style', 'text', 'Tag', '1abc');
uicontrol(f, 'Style', 'text');
hid = uicontrol(f, 'Style', 'text', 'Tag', 'hidden', 'HandleVisibility', 'off');
ax = axes(f, 'Tag', 'ax1');
ln = line(ax, [0 1], [0 1], 'Tag', 'ln1');

% --- guihandles ---------------------------------------------------------------------------------
h = guihandles(f);
names = setdiff(fieldnames(h), {'scribeOverlay'}, 'stable');
fprintf('CHK|guihandles_fields|%s|exact\n', strjoin(names', ' '));
fprintf('CHK|guihandles_class|%s %s|exact\n', class(h), mat2str(size(h)));
fprintf('CHK|guihandles_figure|%d|exact\n', h.mainFig == f);
fprintf('CHK|guihandles_hidden|%d|exact\n', h.hidden == hid);
fprintf('CHK|guihandles_shared_tag|%s %d %d|exact\n', mat2str(size(h.go)), h.go(1) == b2, h.go(2) == b);
fprintf('CHK|guihandles_line|%d|exact\n', h.ln1 == ln);
fprintf('CHK|guihandles_from_control|%d|exact\n', isequal(fieldnames(guihandles(b)), fieldnames(h)));
fprintf('CHK|guihandles_from_line|%d|exact\n', isequal(fieldnames(guihandles(ln)), fieldnames(h)));
g = figure('Visible', 'off', 'MenuBar', 'none', 'ToolBar', 'none');
he = guihandles(g);
fprintf('CHK|guihandles_untagged_figure|%s %s %d|exact\n', class(he), mat2str(size(he)), isempty(he));
he = guihandles;
fprintf('CHK|guihandles_bare_is_gcf|%s|exact\n', class(he));
delete(g);
u2_err('guihandles_not_a_handle', @() guihandles(5.5));
u2_err('guihandles_two', @() guihandles([f f]));
u2_err('guihandles_text', @() guihandles('a'));
u2_err('guihandles_root', @() guihandles(0));
u2_err('guihandles_two_args', @() guihandles(f, 1));

% --- guidata ------------------------------------------------------------------------------------
u2_chk('guidata_before', @() guidata(f));
s.a = 1; s.b = 'two';
guidata(f, s);
fprintf('CHK|guidata_after|%d %s|exact\n', guidata(f).a, guidata(f).b);
fprintf('CHK|guidata_from_child|%s|exact\n', guidata(b).b);
fprintf('CHK|guidata_from_line|%s|exact\n', guidata(ln).b);
fprintf('CHK|guidata_is_appdata|%s %d|exact\n', strjoin(fieldnames(getappdata(f))', ' '), isappdata(f, 'UsedByGUIData_m'));
s.a = 5;
fprintf('CHK|guidata_is_a_copy|%d|exact\n', guidata(f).a);
guidata(b, 42);
u2_chk('guidata_set_through_child', @() guidata(f));
guidata(f, []);
fprintf('CHK|guidata_empty_removes|%d|exact\n', isappdata(f, 'UsedByGUIData_m'));
u2_chk('guidata_after_remove', @() guidata(f));
guidata(f, 1); guidata(f, '');
fprintf('CHK|guidata_empty_text_removes|%d|exact\n', isappdata(f, 'UsedByGUIData_m'));
guidata(f, 1); guidata(f, {});
fprintf('CHK|guidata_empty_cell_removes|%d|exact\n', isappdata(f, 'UsedByGUIData_m'));
guidata(f, 0);
fprintf('CHK|guidata_zero_stays|%d|exact\n', isappdata(f, 'UsedByGUIData_m'));
guidata(f, []);
u2_err('guidata_none', @() guidata());
u2_err('guidata_not_a_handle', @() guidata(5.5));
u2_err('guidata_root', @() guidata(0));
u2_err('guidata_two', @() guidata([f f]));
u2_err('guidata_text', @() guidata('a', 1));
u2_err('guidata_three', @() guidata(f, 1, 2));
d = uicontrol(f); delete(d);
u2_err('guidata_deleted', @() guidata(d));

% --- application data ---------------------------------------------------------------------------
setappdata(f, 'one', 1);
setappdata(f, 'two', 'b');
fprintf('CHK|appdata_names|%s|exact\n', strjoin(fieldnames(getappdata(f))', ' '));
u2_chk('appdata_get', @() getappdata(f, 'one'));
u2_chk('appdata_get_missing', @() getappdata(f, 'none'));
fprintf('CHK|appdata_is|%d %d|exact\n', isappdata(f, 'none'), isappdata(f, 'one'));
u2_err('appdata_rm_missing', @() rmappdata(f, 'none'));
u2_err('appdata_rm', @() rmappdata(f, 'one'));
setappdata(f, 'three', 3);
fprintf('CHK|appdata_names_after|%s|exact\n', strjoin(fieldnames(getappdata(f))', ' '));
v = [1 2 3]; setappdata(f, 'vec', v); v(1) = 9;
u2_chk('appdata_is_a_copy', @() getappdata(f, 'vec'));
u2_err('appdata_set_number_name', @() setappdata(f, 5, 1));
u2_err('appdata_set_bad_name', @() setappdata(f, 'not a name', 1));
u2_err('appdata_set_cell_name', @() setappdata(f, {'x1', 'x2'}, {10, 20}));
u2_err('appdata_set_two_args', @() setappdata(f, 'a'));
u2_err('appdata_get_not_a_handle', @() getappdata(5.5, 'a'));
u2_err('appdata_set_not_a_handle', @() setappdata(5.5, 'a', 1));
u2_err('appdata_is_not_a_handle', @() isappdata(5.5, 'a'));
u2_err('appdata_rm_not_a_handle', @() rmappdata(5.5, 'a'));
setappdata(0, 'rootdata', 7);
fprintf('CHK|appdata_root|%d %d|exact\n', getappdata(0, 'rootdata'), isappdata(0, 'rootdata'));
rmappdata(0, 'rootdata');
fprintf('CHK|appdata_root_removed|%d|exact\n', isappdata(0, 'rootdata'));
setappdata(b, 'onbutton', 3);
fprintf('CHK|appdata_on_control|%d %d|exact\n', getappdata(b, 'onbutton'), isappdata(f, 'onbutton'));
e = getappdata(a);
fprintf('CHK|appdata_none|%s %d %s|exact\n', class(e), numel(fieldnames(e)), mat2str(size(e)));
delete(f);
