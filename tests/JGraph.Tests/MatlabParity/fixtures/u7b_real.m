% record: -noFigureWindows
% U7b of the app-building plan (ADR 0205): an App Designer file as App Designer saves it. U7's
% .mlapp fixtures are built from text and hold the code alone; helpers/U7bApp.mlapp was written by
% R2025b's own serializer (tools/matlab-checklist/ui-probes/u7b/u7b_build.m), so beside the code it
% holds the component tree, App Designer's copy of the code and the package metadata. It is found
% and run as any class file is.
u2_chk('which', @() u7_ext(which('U7bApp')));
u2_chk('exist', @() exist('U7bApp'));
u2_chk('exist_class', @() exist('U7bApp', 'class'));
text = evalc('type U7bApp');
lines = strsplit(strtrim(text), newline);
u2_chk('type_first_line', @() strtrim(lines{1}));
u2_chk('type_line_count', @() numel(lines));

% --- it runs ---------------------------------------------------------------------------------------
app = U7bApp(5);
u2_chk('class', @() class(app));
u2_chk('isa_appbase', @() isa(app, 'matlab.apps.AppBase'));
u2_chk('properties', @() strjoin(properties(app)', ','));
u2_chk('startup_log', @() strjoin(app.Log, ','));
u2_chk('startup_amp', @() app.AmpField.Value);
u2_chk('figure_name', @() app.UIFigure.Name);
u2_chk('figure_position', @() app.UIFigure.Position(3:4));
u2_chk('grid_children', @() numel(app.GridLayout.Children));
u2_chk('grid_columns', @() numel(app.GridLayout.ColumnWidth));
u2_chk('button_text', @() app.GoButton.Text);
u2_chk('button_cell', @() [app.ClearButton.Layout.Row app.ClearButton.Layout.Column]);
u2_chk('running_is_app', @() app.UIFigure.RunningAppInstance == app);

% --- its callbacks, the one with a loop and the one with an empty body -----------------------------
go = app.GoButton.ButtonPushedFcn;
go(app.GoButton, []);
u2_chk('pushed_log', @() strjoin(app.Log, ','));
clr = app.ClearButton.ButtonPushedFcn;
clr(app.ClearButton, []);
u2_chk('empty_callback_log_count', @() numel(app.Log));
u6_id('private_callback_from_outside', @() GoButtonPushed(app, []));

% --- its lifetime and its arguments ----------------------------------------------------------------
fig = app.UIFigure;
delete(app);
u2_chk('figure_goes_with_app', @() isvalid(fig));
plain = U7bApp;
u2_chk('no_argument_amp', @() plain.AmpField.Value);
u2_chk('no_argument_log', @() strjoin(plain.Log, ','));
delete(plain.UIFigure);
u2_chk('app_goes_with_figure', @() isvalid(plain));
u6_id('too_many_arguments', @() U7bApp(1, 2));
