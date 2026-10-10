% record: -noFigureWindows
% U7 of the app-building plan (ADR 0204): an App Designer app from its exported .m file. A class
% written < matlab.apps.AppBase builds its figure, registers with it, runs its startup function
% and lives as long as the figure does: closing the figure deletes the app, deleting the app
% closes the figure, and clearing the variable does neither.
global vlog_text
vlog_text = '';

% --- construction --------------------------------------------------------------------------------
app = U7App(3, 'ramp');
u2_chk('class', @() class(app));
u2_chk('isa_appbase', @() isa(app, 'matlab.apps.AppBase'));
u2_chk('isa_handle', @() isa(app, 'handle'));
u2_chk('superclasses', @() strjoin(superclasses(app)', ','));
u2_chk('properties', @() strjoin(properties(app)', ','));
u2_chk('methods', @() strjoin(unique(methods(app))', ','));
u2_chk('methods_of_base', @() strjoin(unique(methods('matlab.apps.AppBase'))', ','));
u2_chk('startup_log', @() u6_log());
u2_chk('startup_amp', @() app.AmpField.Value);
u2_chk('startup_name', @() app.NameField.Value);
u2_chk('startup_drawn', @() numel(app.UIAxes.Children));
u2_chk('startup_title', @() app.UIAxes.Title.String);
u2_chk('startup_ydata', @() app.UIAxes.Children(1).YData(end));
fig = app.UIFigure;
u2_chk('figure_name', @() fig.Name);
u2_chk('figure_visible', @() fig.Visible);
u2_chk('figure_handlevisibility', @() fig.HandleVisibility);
u2_chk('figure_isa', @() isa(fig, 'matlab.ui.Figure'));
u2_chk('figure_count', @() u7_figs());
u2_chk('grid_children', @() numel(app.GridLayout.Children));
u2_chk('button_cell', @() [app.PlotButton.Layout.Row app.PlotButton.Layout.Column]);
u2_chk('axes_isa', @() isa(app.UIAxes, 'matlab.ui.control.UIAxes'));
u2_chk('isvalid', @() isvalid(app));

% --- the figure knows its app --------------------------------------------------------------------
u2_chk('isprop_running', @() isprop(fig, 'RunningAppInstance'));
u2_chk('running_is_app', @() fig.RunningAppInstance == app);
u2_chk('running_file', @() u7_ext(fig.RunningInstanceFullFileName));
u2_chk('running_get', @() get(fig, 'RunningAppInstance') == app);
other = uifigure('Visible', 'off');
u2_chk('isprop_running_plain', @() isprop(other, 'RunningAppInstance'));
fprintf('CHK|running_plain|%s|exact\n', u7_id(@() other.RunningAppInstance)); % open item 68: the dot's words
u5_run('running_write', @() u6_setp(fig, 'RunningAppInstance', 5));
delete(other);

% --- callbacks -----------------------------------------------------------------------------------
cb = app.PlotButton.ButtonPushedFcn;
u2_chk('callback_class', @() class(cb));
u2_chk('callback_nargin', @() nargin(cb));
fprintf('CHK|callback_text|%s|div=ADR0204\n', func2str(cb));
cb(app.PlotButton, 'evt');
u2_chk('callback_log', @() u6_log());
u2_chk('callback_presses', @() presses(app));
u5_run('callback_one_argument', @() cb(app.PlotButton));
u5_run('callback_no_arguments', @() cb());
u5_run('callback_three_arguments', @() cb(1, 2, 3));
u2_chk('callback_arity_log', @() u6_log());
app.AmpField.Value = 4;
cb = app.AmpField.ValueChangedFcn;
cb([], struct('Value', 4));
u2_chk('callback_amp_log', @() u6_log());
cb = app.NameField.ValueChangedFcn;
cb([], []);
u2_chk('callback_no_event_log', @() u6_log());
u5_run('callback_no_event_one', @() cb(1));
u6_log();
app.Scale = 2;
cb = app.PlotButton.ButtonPushedFcn;
cb([], []);
u2_chk('redraw_ydata', @() app.UIAxes.Children(1).YData(end));
u2_chk('redraw_children', @() numel(app.UIAxes.Children));
u6_log();

% --- what is private stays private ---------------------------------------------------------------
u5_run('private_method', @() redraw(app));
u5_run('private_method_dot', @() app.redraw());
u5_run('private_callback', @() PlotButtonPushed(app, []));
u5_run('private_startup', @() startupFcn(app));
u5_run('private_create', @() createComponents(app));
u2_chk('private_read', @() app.Presses);
u5_run('private_write', @() u6_setp(app, 'Presses', 9));
u5_run('protected_callbackfcn', @() createCallbackFcn(app, @presses, false));
u5_run('protected_register', @() registerApp(app, fig));
u5_run('protected_startup', @() runStartupFcn(app, @presses));
u5_run('protected_running', @() getRunningApp(app));
u5_run('protected_autoresize', @() setAutoResize(app, fig, true));
u5_run('typed_property_number', @() u6_setp(app, 'UIFigure', 5.5));
u5_run('typed_property_button', @() u6_setp(app, 'UIFigure', app.PlotButton));
u2_chk('typed_property_kept', @() isequal(app.UIFigure, fig));
u2_chk('private_log', @() u6_log());

% --- closing the figure deletes the app ----------------------------------------------------------
close(fig);
u2_chk('close_log', @() u6_log());
u2_chk('close_app_valid', @() isvalid(app));
u2_chk('close_figure_valid', @() isvalid(fig));
u2_chk('close_figure_count', @() u7_figs());
u5_run('dead_app_read', @() app.Scale);
u5_run('dead_app_method', @() presses(app));
u5_run('dead_app_delete', @() delete(app));
u2_chk('dead_log', @() u6_log());

% --- deleting the app closes the figure ----------------------------------------------------------
app = U7App;
u2_chk('default_log', @() u6_log());
u2_chk('default_amp', @() app.AmpField.Value);
fig = app.UIFigure;
delete(app);
u2_chk('delete_log', @() u6_log());
u2_chk('delete_app_valid', @() isvalid(app));
u2_chk('delete_figure_valid', @() isvalid(fig));
u2_chk('delete_figure_count', @() u7_figs());

% --- deleting the figure deletes the app ---------------------------------------------------------
app = U7App(1);
fig = app.UIFigure;
fig.CloseRequestFcn = '';
fig.DeleteFcn = @(s, e) u7_note('deletefcn');
addlistener(fig, 'ObjectBeingDestroyed', @(s, e) u7_note('figure listener'));
addlistener(app, 'ObjectBeingDestroyed', @(s, e) u7_note('app listener'));
u6_log();
delete(fig);
u2_chk('figure_delete_log', @() u6_log());
u2_chk('figure_delete_app_valid', @() isvalid(app));

% --- the app outlives its variable ---------------------------------------------------------------
app = U7App(5, 'kept');
fig = app.UIFigure;
btn = app.PlotButton;
clear app
u2_chk('clear_log', @() u6_log());
u2_chk('clear_figure_valid', @() isvalid(fig));
cb = btn.ButtonPushedFcn;
cb(btn, []);
u2_chk('clear_callback_log', @() u6_log());
again = fig.RunningAppInstance;
u2_chk('clear_app_valid', @() isvalid(again));
u2_chk('clear_presses', @() presses(again));
clear again cb
u2_chk('clear_again_figure_valid', @() isvalid(fig));
close(fig);
u2_chk('clear_close_log', @() u6_log());
u2_chk('clear_close_figure_valid', @() isvalid(fig));

% --- called for nothing --------------------------------------------------------------------------
clear ans
U7App(6, 'bare');
u2_chk('bare_ans', @() exist('ans', 'var'));
u2_chk('bare_log', @() u6_log());
u2_chk('bare_figure_count', @() u7_figs());
figs = findall(groot, 'Type', 'figure', 'Name', 'U7 App');
u2_chk('bare_found', @() numel(figs));
u2_chk('bare_app_amp', @() figs(1).RunningAppInstance.AmpField.Value);
delete(figs);
u2_chk('bare_delete_log', @() u6_log());
u2_chk('bare_delete_count', @() u7_figs());

% --- a startup function that fails, and arguments it does not take -------------------------------
u5_run('too_many_arguments', @() U7App(1, 'a', 3));
u2_chk('too_many_log', @() u6_log());
u2_chk('too_many_count', @() u7_figs());
u5_run('startup_fails', @() U7Plain('fail'));
u2_chk('startup_fails_log', @() u6_log());
u2_chk('startup_fails_count', @() u7_figs());
u5_run('startup_bad_argument', @() U7App('x'));
u2_chk('startup_bad_log', @() u6_log());
u2_chk('startup_bad_count', @() u7_figs());

% --- the startup function sees the figure --------------------------------------------------------
app = U7Plain;
u2_chk('plain_log', @() u6_log());
seen = strsplit(app.Seen);
u2_chk('plain_seen', @() [seen{1} ' ' seen{3}]);
u2_chk('plain_seen_current', @() seen{2});
u2_chk('plain_handlevisibility', @() app.UIFigure.HandleVisibility);
delete(app);
app = U7Plain('callback');
seen = strsplit(app.Seen);
u2_chk('callback_seen', @() [seen{1} ' ' seen{3}]);
fprintf('CHK|callback_seen_current|%s|exact\n', seen{2}); % open item 55: current while it starts
u2_chk('callback_handlevisibility', @() app.UIFigure.HandleVisibility);
u6_log();

% --- the protected methods, from inside ----------------------------------------------------------
h = wrap(app, true);
u2_chk('wrap_class', @() class(h));
h(1, 2);
u2_chk('wrap_event_log', @() u6_log());
h = wrap(app, false);
h(1, 2);
u2_chk('wrap_no_event_log', @() u6_log());
h = wrapAnonymous(app);
h('s', 5);
u2_chk('wrap_anonymous_log', @() u6_log());
h = wrapPublic(app);
h([], []);
u2_chk('wrap_public_log', @() u6_log());
u2_chk('running_same', @() running(app) == app);
u2_chk('running_class', @() class(running(app)));
u5_run('register_again', @() again(app));
u2_chk('register_again_same', @() app.UIFigure.RunningAppInstance == app);
u5_run('register_number', @() registerOther(app, 5.5));
u5_run('autoresize_off', @() resize(app, false));
u2_chk('autoresize_read', @() app.UIFigure.AutoResizeChildren);
u5_run('autoresize_on', @() resize(app, true));
u2_chk('autoresize_read_on', @() app.UIFigure.AutoResizeChildren);
u5_run('start_again', @() start(app, @poke));
u5_run('start_anonymous', @() start(app, @(a) u7_note(['anonymous ' class(a)])));
u5_run('start_number', @() start(app, 5));
u2_chk('start_log', @() u6_log());
u5_run('wrap_bad_flag', @() wrap(app, 'yes'));
u5_run('wrap_called_bad_flag', @() feval(wrap(app, 2), 1, 2));
u6_log();
h = wrap(app, true);
fig = app.UIFigure;
delete(app);
u5_run('wrap_after_delete', @() h(1, 2));
u2_chk('wrap_after_delete_log', @() u6_log());
u2_chk('plain_figure_valid', @() isvalid(fig));

% --- one running instance ------------------------------------------------------------------------
a = U7Single(1);
u2_chk('single_log', @() u6_log());
b = U7Single(2);
u2_chk('single_second_log', @() u6_log());
u2_chk('single_same', @() a == b);
u2_chk('single_started', @() b.Started);
u2_chk('single_figure_count', @() u7_figs());
U7Single(3);
u2_chk('single_bare_log', @() u6_log());
u2_chk('single_bare_count', @() u7_figs());
other = U7Plain;
u2_chk('single_other_running', @() running(other) == other);
delete(other);
u6_log();
delete(a);
u2_chk('single_delete_log', @() u6_log());
u2_chk('single_b_valid', @() isvalid(b));
c = U7Single(4);
u2_chk('single_new_started', @() c.Started);
u2_chk('single_new_log', @() u6_log());
delete(c);

% --- the base class itself -----------------------------------------------------------------------
u5_run('base_alone', @() matlab.apps.AppBase);
u2_chk('base_alone_class', @() class(matlab.apps.AppBase));
u2_chk('base_properties', @() numel(properties('matlab.apps.AppBase')));
u2_chk('base_superclasses', @() strjoin(superclasses('matlab.apps.AppBase')', ','));
u2_chk('base_exist', @() exist('matlab.apps.AppBase', 'class'));
lastwarn('');
s = saveobj(U7Plain);
[~, wid] = lastwarn;
u2_chk('saveobj_value', @() s);
u2_chk('saveobj_warning', @() wid);
u5_run('loadobj', @() matlab.apps.AppBase.loadobj(struct()));
delete(findall(groot, 'Type', 'figure'));
u2_chk('end_log', @() u6_log());
u2_chk('end_count', @() u7_figs());
