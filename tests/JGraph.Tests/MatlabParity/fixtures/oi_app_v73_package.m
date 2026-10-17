% record: -noFigureWindows
% Open items 57 and 58 (ADR 0239): an App Designer file whose model is a version 7.3 MAT-file.
% helpers/U7bApp73.mlapp is this project's U7b app written by R2025b's own serializer under
% MATLAB's version 7.3 MAT-file preference (tools/matlab-checklist/ui-probes/oi57/oi57_build.m), so
% its appdesigner/appModel.mat is HDF5 - the form one shipped app keeps its model in, and the form
% any app saved under that preference keeps it in. It is found and run as any class file is; what
% JGraph's save does to its model is the probe oi57_verify's to say, with R2025b reading the result.
u2_chk('which', @() u7_ext(which('U7bApp73')));
u2_chk('exist', @() exist('U7bApp73'));
u2_chk('exist_class', @() exist('U7bApp73', 'class'));
text = evalc('type U7bApp73');
lines = strsplit(strtrim(text), newline);
u2_chk('type_first_line', @() strtrim(lines{1}));
u2_chk('type_line_count', @() numel(lines));

% --- it runs ---------------------------------------------------------------------------------------
app = U7bApp73(7);
u2_chk('class', @() class(app));
u2_chk('isa_appbase', @() isa(app, 'matlab.apps.AppBase'));
u2_chk('properties', @() strjoin(properties(app)', ','));
u2_chk('startup_log', @() strjoin(app.Log, ','));
u2_chk('startup_amp', @() app.AmpField.Value);
u2_chk('figure_name', @() app.UIFigure.Name);
u2_chk('grid_children', @() numel(app.GridLayout.Children));
u2_chk('button_text', @() app.GoButton.Text);
u2_chk('running_is_app', @() app.UIFigure.RunningAppInstance == app);
go = app.GoButton.ButtonPushedFcn;
go(app.GoButton, []);
u2_chk('pushed_log', @() strjoin(app.Log, ','));
clr = app.ClearButton.ButtonPushedFcn;
clr(app.ClearButton, []);
u2_chk('empty_callback_log_count', @() numel(app.Log));
u6_id('private_callback_from_outside', @() GoButtonPushed(app, []));
fig = app.UIFigure;
delete(app);
u2_chk('figure_goes_with_app', @() isvalid(fig));
plain = U7bApp73;
u2_chk('no_argument_amp', @() plain.AmpField.Value);
delete(plain.UIFigure);
u2_chk('app_goes_with_figure', @() isvalid(plain));

% --- the package of code alone (item 58) runs beside it, as the U7 fixtures run it -------------------
u2_chk('code_only_which', @() u7_ext(which('U7MlApp')));
u2_chk('code_only_exist_class', @() exist('U7MlApp', 'class'));
