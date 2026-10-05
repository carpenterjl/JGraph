% record: -noFigureWindows
% U7 of the app-building plan (ADR 0204): the .mlapp file. An .mlapp is a zip whose document part
% holds a class file as text; MATLAB resolves, lists and runs it as it does an .m file, ahead of an
% .m file of the same name in the same folder. The files are built by
% tools/matlab-checklist/ui-probes/u7/mlapp/build_mlapp.py.
global vlog_text
vlog_text = '';
helpers = fileparts(which('u7_note'));

% --- finding it ----------------------------------------------------------------------------------
u2_chk('which', @() u7_ext(which('U7MlApp')));
u2_chk('which_in_helpers', @() strcmp(fileparts(which('U7MlApp')), helpers));
u2_chk('which_with_extension', @() u7_ext(which('U7MlApp.mlapp')));
u2_chk('exist', @() exist('U7MlApp'));
u2_chk('exist_file', @() exist('U7MlApp', 'file'));
u2_chk('exist_with_extension', @() exist('U7MlApp.mlapp', 'file'));
u2_chk('exist_full_path', @() exist(fullfile(helpers, 'U7MlApp.mlapp'), 'file'));
u2_chk('exist_class', @() exist('U7MlApp', 'class'));
u2_chk('exist_plain_class', @() exist('U7InZip', 'class'));
u2_chk('exist_function', @() exist('U7Fn'));
u2_chk('exist_none', @() exist('U7Nothing'));
u2_chk('same_which', @() u7_ext(which('U7Same')));
u2_chk('same_which_all', @() strjoin(cellfun(@u7_ext, which('U7Same', '-all'), 'UniformOutput', false)', ','));
same = U7Same;
u2_chk('same_runs', @() same.Source);
w = what(helpers);
u2_chk('what_has_field', @() isfield(w, 'mlapp'));
u2_chk('what_lists', @() ismember('U7MlApp.mlapp', w.mlapp));
u2_chk('what_m_keeps_twin', @() ismember('U7Same.m', w.m));
u2_chk('what_m_no_mlapp', @() any(endsWith(w.m, '.mlapp')));
u2_chk('isfile', @() isfile(fullfile(helpers, 'U7MlApp.mlapp')));

% --- a class in an .mlapp ------------------------------------------------------------------------
o = U7InZip;
u2_chk('class', @() class(o));
u2_chk('property', @() o.Value);
u2_chk('method', @() twice(o));
u2_chk('static', @() U7InZip.fixed());
[name, inHelpers] = where(o);
u2_chk('mfilename', @() name);
u2_chk('mfilename_folder', @() inHelpers);
u2_chk('methods', @() strjoin(methods('U7InZip')', ','));
u2_chk('properties', @() strjoin(properties('U7InZip')', ','));
text = evalc('type U7InZip');
lines = strsplit(strtrim(text), newline);
u2_chk('type_first_line', @() strtrim(lines{1}));
u2_chk('type_line_count', @() numel(lines));
text = evalc('type U7InZip.mlapp');
lines = strsplit(strtrim(text), newline);
u2_chk('type_with_extension', @() strtrim(lines{1}));

% --- what is not a class file --------------------------------------------------------------------
u6_id('bare_zip', @() U7Bare);
u2_chk('bare_zip_exist', @() exist('U7Bare'));
u2_chk('function_in_mlapp', @() U7Fn(1));
u6_id('wrong_name', @() U7Wrong);
u6_id('wrong_name_other', @() U7Other);
u6_id('run_mlapp', @() run(fullfile(helpers, 'U7InZip.mlapp')));

% --- where an error is ---------------------------------------------------------------------------
try
    fail(U7Throws);
catch err
    u2_chk('error_identifier', @() err.identifier);
    u2_chk('error_message', @() err.message);
    u2_chk('error_stack_name', @() err.stack(1).name);
    u2_chk('error_stack_line', @() err.stack(1).line);
    u2_chk('error_stack_file', @() u7_ext(err.stack(1).file));
end

% --- the app, from its .mlapp --------------------------------------------------------------------
app = U7MlApp(3, 'ramp');
u2_chk('app_class', @() class(app));
u2_chk('app_isa', @() isa(app, 'matlab.apps.AppBase'));
u2_chk('app_startup_log', @() u6_log());
u2_chk('app_amp', @() app.AmpField.Value);
u2_chk('app_title', @() app.UIAxes.Title.String);
u2_chk('app_ydata', @() app.UIAxes.Children(1).YData(end));
fig = app.UIFigure;
u2_chk('app_figure_name', @() fig.Name);
u2_chk('app_running_file', @() u7_ext(fig.RunningInstanceFullFileName));
u2_chk('app_running', @() fig.RunningAppInstance == app);
cb = app.PlotButton.ButtonPushedFcn;
cb(app.PlotButton, 'evt');
u2_chk('app_callback_log', @() u6_log());
u2_chk('app_presses', @() presses(app));
u5_run('app_private', @() redraw(app));
clear app cb
u2_chk('app_cleared_valid', @() isvalid(fig));
close(fig);
u2_chk('app_close_log', @() u6_log());
u2_chk('app_close_valid', @() isvalid(fig));
U7MlApp(8);
figs = findall(groot, 'Type', 'figure', 'Name', 'U7 MlApp');
u2_chk('app_bare_found', @() numel(figs));
u2_chk('app_bare_amp', @() figs(1).RunningAppInstance.AmpField.Value);
delete(figs);
u2_chk('app_bare_log', @() u6_log());
u2_chk('end_count', @() u7_figs());
