% record: -noFigureWindows
% U4 of the app-building plan (ADR 0201): the classic dialogs that do not block - dialog, msgbox and
% its wrappers errordlg, warndlg and helpdlg, and waitbar - as the figures they are: their object
% trees, R2025b's layout constants, the forms of the call and the refusals. The three that block,
% and the system's own dialogs, refuse without somebody to answer them.

% --- dialog -------------------------------------------------------------------------------------
d = dialog;
fprintf('CHK|dialog_defaults|[%s] %s %s %s [%s] %s %s %s %s %s|exact\n', get(d, 'Name'), get(d, 'WindowStyle'), ...
    char(get(d, 'Resize')), get(d, 'Units'), get(d, 'Tag'), get(d, 'HandleVisibility'), char(get(d, 'IntegerHandle')), ...
    char(get(d, 'NumberTitle')), get(d, 'MenuBar'), char(get(d, 'Visible')));
fprintf('CHK|dialog_buttondown|%s|exact\n', get(d, 'ButtonDownFcn'));
fprintf('CHK|dialog_color|%s|exact\n', mat2str(get(d, 'Color'), 4));
fprintf('CHK|dialog_is_figure|%s %d|exact\n', get(d, 'Type'), isempty(allchild(d)));
delete(d);
d = dialog('Name', 'N', 'WindowStyle', 'normal', 'Position', [10 20 300 100], 'Visible', 'off', 'Tag', 't', 'Color', [1 0 0], 'Resize', 'on');
fprintf('CHK|dialog_options|[%s] %s %s %s [%s] %s %s %s|exact\n', get(d, 'Name'), get(d, 'WindowStyle'), char(get(d, 'Resize')), ...
    mat2str(get(d, 'Position')), get(d, 'Tag'), mat2str(get(d, 'Color')), char(get(d, 'Visible')), get(d, 'HandleVisibility'));
delete(d);
u2_err('dialog_odd', @() dialog('Name'));
u2_err('dialog_unknown', @() dialog('NoSuch', 1));
u2_err('dialog_bad_style', @() dialog('WindowStyle', 'bad'));
u2_err('dialog_string_arguments', @() delete(dialog("Name", "s")));
f = figure('Visible', 'off');
for w = {'normal', 'modal', 'docked', 'alwaysontop'}
    set(f, 'WindowStyle', w{1});
    fprintf('CHK|windowstyle_%s|%s|exact\n', w{1}, get(f, 'WindowStyle'));
end
set(f, 'WindowStyle', 'normal');
u2_err('windowstyle_bad', @() set(f, 'WindowStyle', 'bad'));
delete(f);

% --- msgbox and its wrappers --------------------------------------------------------------------
cases = {
    'one_arg',          @() msgbox('Hello there')
    'title',            @() msgbox('Hello there', 'Title')
    'modal',            @() msgbox('Hello there', 'Title', 'modal')
    'warn',             @() msgbox('Hello there', 'Title', 'warn')
    'help',             @() msgbox('Hello there', 'Title', 'help')
    'error',            @() msgbox('Hello there', 'Title', 'error')
    'none',             @() msgbox('Hello there', 'Title', 'none')
    'icon_modal',       @() msgbox('Hello there', 'Title', 'error', 'modal')
    'two_lines',        @() msgbox({'First line', 'Second line is longer'}, 'T')
    'char_matrix',      @() msgbox(['ab '; 'cde'], 'T')
    'newline',          @() msgbox(sprintf('one\ntwo'), 'T')
    'string_scalar',    @() msgbox("A string", "T")
    'string_array',     @() msgbox(["a" "b"], "T")
    'empty',            @() msgbox('', 'T')
    'long',             @() msgbox(repmat('word ', 1, 40), 'T')
    'five_lines_icon',  @() msgbox({'1', '2', '3', '4', '5'}, 'T', 'warn')
    'struct_mode',      @() msgbox('x', 'T', struct('WindowStyle', 'modal', 'Interpreter', 'tex'))
    'struct_icon',      @() msgbox('x', 'T', 'help', struct('WindowStyle', 'non-modal', 'Interpreter', 'none'))
    'replace',          @() msgbox('x', 'T', 'replace')
    'custom',           @() msgbox('x', 'T', 'custom', ones(8, 8, 3) / 2)
    'custom_map',       @() msgbox('x', 'T', 'custom', magic(4), gray(16))
    'six_args',         @() msgbox('x', 'T', 'custom', magic(4), gray(16), 'modal')
    'errordlg_none',    @() errordlg()
    'errordlg_text',    @() errordlg('Bad value')
    'errordlg_title',   @() errordlg('Bad value', 'Oops')
    'errordlg_modal',   @() errordlg('Bad value', 'Oops', 'modal')
    'errordlg_cell',    @() errordlg({'a', 'b'}, 'Oops')
    'warndlg_none',     @() warndlg()
    'warndlg_modal',    @() warndlg('Careful', 'W', 'modal')
    'warndlg_struct',   @() warndlg('Careful', 'W', struct('WindowStyle', 'modal', 'Interpreter', 'tex'))
    'helpdlg_none',     @() helpdlg()
    'helpdlg_title',    @() helpdlg('Help text', 'H')
    };
for k = 1:size(cases, 1)
    h = cases{k, 2}();
    u4_box(cases{k, 1}, h);
    delete(findall(0, 'Type', 'figure'));
end
refused = {
    'custom_no_data',   @() msgbox('x', 'T', 'custom')
    'custom_cell_data', @() msgbox('x', 'T', 'custom', {1})
    'no_args',          @() msgbox()
    'seven_args',       @() msgbox('x', 'T', 'custom', magic(4), gray(16), 'modal', 1)
    'numeric_text',     @() msgbox(5)
    'numeric_title',    @() msgbox('x', 5)
    'bad_struct',       @() msgbox('x', 'T', struct('WindowStyle', 'modal'))
    'bad_interpreter',  @() msgbox('x', 'T', struct('WindowStyle', 'modal', 'Interpreter', 'bogus'))
    'errordlg_number',  @() errordlg(5)
    'errordlg_four',    @() errordlg('a', 'b', 'modal', 4)
    'helpdlg_three',    @() helpdlg('Help text', 'H', 'modal')
    };
for k = 1:size(refused, 1)
    u2_err(['refused_' refused{k, 1}], refused{k, 2});
    delete(findall(0, 'Type', 'figure'));
end
lastwarn('');
h = msgbox('Hello there', 'Title', 'bogus');
[~, id] = lastwarn;
fprintf('CHK|bad_icon_warning|%s %d|exact\n', id, isempty(findall(h, 'Tag', 'IconAxes')));
delete(h);
lastwarn('');
h = msgbox('x', 'T', 'warn', magic(4), gray(16));
[~, id] = lastwarn;
fprintf('CHK|five_args_warning|%s %d|exact\n', id, isempty(findall(h, 'Tag', 'IconAxes')));
delete(h);

% --- replacing a message box of the same name ---------------------------------------------------
a = msgbox('first', 'Same');
b = msgbox('second', 'Same');
fprintf('CHK|nonmodal_adds|%d %d|exact\n', isequal(a, b), numel(findall(0, 'Type', 'figure')));
c = msgbox('third', 'Same', 'replace');
fprintf('CHK|replace_takes_newest|%d %d %d %d %d|exact\n', isequal(c, a), isequal(c, b), numel(findall(0, 'Type', 'figure')), ishghandle(a), ishghandle(b));
tx = findall(c, 'Type', 'text');
fprintf('CHK|replace_text|%s|exact\n', char(string(get(tx, 'String'))));
c2 = msgbox('fourth', 'Same', 'modal');
fprintf('CHK|modal_replaces_too|%d %d %s|exact\n', isequal(c2, c), numel(findall(0, 'Type', 'figure')), get(c2, 'WindowStyle'));
h1 = helpdlg('h1', 'HT'); h2 = helpdlg('h2', 'HT');
fprintf('CHK|helpdlg_replaces|%d|exact\n', isequal(h1, h2));
e1 = errordlg('e1', 'ET'); e2 = errordlg('e2', 'ET');
fprintf('CHK|errordlg_adds|%d|exact\n', isequal(e1, e2));
e3 = errordlg('e3', 'ET', 'on');
fprintf('CHK|errordlg_on_replaces|%d|exact\n', isequal(e3, e1) || isequal(e3, e2));
delete(findall(0, 'Type', 'figure'));

% --- a message box is an ordinary figure to wait on ---------------------------------------------
m = msgbox('wait', 'W');
fprintf('CHK|msgbox_callbacks|%s %s|exact\n', get(m, 'CloseRequestFcn'), class(get(m, 'KeyPressFcn')));
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) delete(m)); start(tm);
t = tic; uiwait(m); e = toc(t); delete(tm);
fprintf('CHK|uiwait_msgbox|%d %d|exact\n', e >= 0.4 && e < 6, ishghandle(m));

% --- waitbar ------------------------------------------------------------------------------------
w = waitbar(0.25, 'Working...');
fprintf('CHK|waitbar_figure|[%s] %s %s %s [%s] %s %s %s %s|exact\n', get(w, 'Name'), get(w, 'WindowStyle'), char(get(w, 'Resize')), ...
    get(w, 'Units'), get(w, 'Tag'), get(w, 'HandleVisibility'), char(get(w, 'IntegerHandle')), char(get(w, 'NumberTitle')), get(w, 'MenuBar'));
fprintf('CHK|waitbar_size|%s|exact\n', mat2str(get(w, 'Position') * [0 0; 0 0; 1 0; 0 1], 6));
fprintf('CHK|waitbar_busy|%s %s|exact\n', get(w, 'BusyAction'), char(get(w, 'Interruptible')));
fprintf('CHK|waitbar_appdata|%s|exact\n', strjoin(fieldnames(getappdata(w))', ' '));
hs = getappdata(w, 'TMWWaitbar_handles');
fprintf('CHK|waitbar_handles|%s|exact\n', strjoin(fieldnames(hs)', ' '));
fprintf('CHK|waitbar_value|%g|exact\n', getappdata(w, 'TMWWaitbar_value'));
ax = findall(w, 'Type', 'axes');
fprintf('CHK|waitbar_axes|%s %s %s %s %s|exact\n', get(ax, 'Units'), mat2str(get(ax, 'Position'), 6), mat2str(get(ax, 'XLim')), mat2str(get(ax, 'YLim')), char(get(ax, 'Visible')));
ttl = get(ax, 'Title');
fprintf('CHK|waitbar_title|%s %d %s|exact\n', get(ttl, 'String'), isequal(ttl, hs.axesTitle), char(get(ttl, 'Visible')));
bar = findall(w, 'Type', 'uiprogressindicator');
fprintf('CHK|waitbar_indicator|%s %g %s %s %s|exact\n', mat2str(get(bar, 'Position'), 6), get(bar, 'Value'), char(get(bar, 'Indeterminate')), ...
    mat2str(get(bar, 'ProgressColor'), 4), get(bar, 'HandleVisibility'));
fprintf('CHK|waitbar_indicator_is_progressbar|%d|exact\n', isequal(bar, hs.progressbar));
r = waitbar(0.5, w);
fprintf('CHK|waitbar_update|%d %g %g|exact\n', isequal(r, w), getappdata(w, 'TMWWaitbar_value'), get(bar, 'Value'));
waitbar(0.75, w, 'New message');
fprintf('CHK|waitbar_message|%s %g|exact\n', get(ttl, 'String'), get(bar, 'Value'));
waitbar(2, w);
fprintf('CHK|waitbar_above_one|%g|exact\n', get(bar, 'Value'));
waitbar(-1, w);
fprintf('CHK|waitbar_below_zero|%g|exact\n', get(bar, 'Value'));
r = waitbar(0.3);
fprintf('CHK|waitbar_bare_updates_newest|%d %g|exact\n', isequal(r, w), get(bar, 'Value'));
u2_err('waitbar_none', @() waitbar());
u2_err('waitbar_text_value', @() waitbar('a'));
u2_err('waitbar_vector_value', @() waitbar([1 2]));
u2_err('waitbar_bad_second', @() waitbar(0.5, 5.5));
u2_err('waitbar_number_message', @() waitbar(0.5, w, 5));
fprintf('CHK|waitbar_survives|%d|exact\n', ishghandle(w));
delete(findall(0, 'Type', 'figure'));
w = waitbar(0, 'Named', 'Name', 'Progress', 'Color', [1 1 1]);
fprintf('CHK|waitbar_options|%s %s|exact\n', get(w, 'Name'), mat2str(get(w, 'Color')));
delete(w);
w = waitbar(0.1, 'Cancel me', 'CreateCancelBtn', 'disp(1)');
btn = findall(w, 'Tag', 'TMWWaitbarCancelButton');
fprintf('CHK|waitbar_cancel|%s %s [%s] %s %s|exact\n', get(btn, 'Style'), get(btn, 'Units'), get(btn, 'String'), mat2str(get(btn, 'Position'), 6), get(btn, 'Callback'));
wp = get(w, 'Position');
fprintf('CHK|waitbar_cancel_figure|%s %g|exact\n', get(w, 'CloseRequestFcn'), wp(3));
fprintf('CHK|waitbar_cancel_height|%.10g|abs=3\n', wp(4));
ax = findall(w, 'Type', 'axes');
bar = findall(w, 'Type', 'uiprogressindicator');
fprintf('CHK|waitbar_cancel_axes|%s %s|exact\n', mat2str(get(ax, 'Position'), 6), mat2str(get(bar, 'Position'), 6));
delete(w);
w = waitbar(0.4);
fprintf('CHK|waitbar_default_message|%s|exact\n', get(get(findall(w, 'Type', 'axes'), 'Title'), 'String'));
delete(w);
w1 = waitbar(0.1, 'a'); w2 = waitbar(0.2, 'b');
fprintf('CHK|waitbar_two|%d|exact\n', isequal(w1, w2));
delete(w1); delete(w2);
w = waitbar(0.1, 'x', 'Visible', 'off');
fprintf('CHK|waitbar_invisible|%s|exact\n', char(get(w, 'Visible')));
delete(w);
lastwarn('');
w = waitbar(0.1, 'x', 'NoSuchProp', 1);
[~, id] = lastwarn;
fprintf('CHK|waitbar_bad_option|%s %d|exact\n', id, ishghandle(w));
delete(w);
u2_err('waitbar_odd_options', @() waitbar(0.1, 'x', 'Name'));
delete(findall(0, 'Type', 'figure'));

% --- the ones that block refuse with nobody to answer -------------------------------------------
u2_err('questdlg_refuses', @() questdlg('a'));
u2_err('questdlg_refuses_before_arguments', @() questdlg());
u2_err('questdlg_seven', @() questdlg('a', 'b', 'c', 'd', 'e', 'f', 'g'));
u2_err('inputdlg_refuses', @() inputdlg('a'));
u2_err('inputdlg_six', @() inputdlg('a', 't', 1, {''}, 'on', 6));
u2_err('listdlg_refuses', @() listdlg('ListString', {'a'}));
u2_err('listdlg_refuses_before_arguments', @() listdlg('Bogus', 1));
u2_err('uigetfile_refuses', @() uigetfile());
u2_err('uiputfile_refuses', @() uiputfile());
u2_err('uigetdir_refuses', @() uigetdir());
u2_err('uisetcolor_refuses', @() uisetcolor());
u2_err('uisetfont_refuses', @() uisetfont());
u2_err('uiopen_refuses', @() uiopen());
u2_err('uisave_refuses', @() uisave());
u2_err('uiload_refuses', @() uiload());
u2_err('uiload_argument', @() uiload(5));
delete(findall(0, 'Type', 'figure'));
