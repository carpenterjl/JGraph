% ds8_accept - the designer plan's acceptance apps (DS8), built in the designer by the local test
% AppDesignerAcceptanceTests.TheAcceptanceAppsAreBuiltSavedAndOpenedWhole with JGRAPH_DS8_OUT naming
% this folder's rt (rt\accept: AcceptForm.mlapp, AcceptGrid.mlapp, AcceptPanels.mlapp and the
% plain-text AcceptFormPlain.m), run in R2025b's -batch and in JGraph's, the two outputs held to each
% other:
%   powershell -ExecutionPolicy Bypass -File .\run-probe.ps1 -Name ds8_accept
%   ..\..\..\..\src\JGraph.Cli\bin\Debug\net8.0\jgraph.exe -batch ds8_accept.m > ds8_accept.jgraph.out
% Each app is constructed, its callbacks called as its window would call them, its state printed and
% the app deleted, so neither engine has a window left to note. Nothing here is engine-specific.
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, 'rt', 'accept'));

% --- AcceptForm: absolute layout, startupFcn(app, n), a callback over the components ---------------
app = AcceptForm(5);
fprintf('form: field %g, spinner %g, drop-down %s, check box %d, label %s\n', ...
    app.EditField.Value, app.Spinner.Value, app.DropDown.Value, app.DoubleCheckBox.Value, app.ResultLabel.Text);
app.ComputeButton.ButtonPushedFcn(app.ComputeButton, []);
fprintf('form: add -> %s\n', app.ResultLabel.Text);
app.DropDown.Value = 'Multiply';
app.DoubleCheckBox.Value = true;
app.ComputeButton.ButtonPushedFcn(app.ComputeButton, []);
fprintf('form: multiply, doubled -> %s\n', app.ResultLabel.Text);
d = app.DatePicker.DisabledDates;
fprintf('form: disabled dates %dx%d %s: %d-%02d-%02d, %d-%02d-%02d\n', size(d, 1), size(d, 2), class(d), ...
    year(d(1)), month(d(1)), day(d(1)), year(d(2)), month(d(2)), day(d(2)));
n = app.Tree.CheckedNodes;
fprintf('form: checked nodes %s\n', strjoin(sort(cellstr(get(n, 'Text'))), ', '));
fprintf('form: figure %s, %d children\n', app.AcceptFormUIFigure.Name, numel(app.AcceptFormUIFigure.Children));
delete(app);

% --- AcceptGrid: Apply Grid Layout, a slider, a gauge and an axes in its cells ---------------------
app = AcceptGrid;
fprintf('grid: %d rows, %d columns; slider columns %s; gauge rows %s; axes rows %s, columns %s\n', ...
    numel(app.GridLayout.RowHeight), numel(app.GridLayout.ColumnWidth), mat2str(app.Slider.Layout.Column), ...
    mat2str(app.Gauge.Layout.Row), mat2str(app.UIAxes.Layout.Row), mat2str(app.UIAxes.Layout.Column));
app.Slider.ValueChangedFcn(app.Slider, struct('Value', 40));
fprintf('grid: gauge %g, %d line(s), last y %g\n', app.Gauge.Value, numel(app.UIAxes.Children), app.UIAxes.Children(1).YData(end));
delete(app);

% --- AcceptPanels: the two-panel responsive template ----------------------------------------------
app = AcceptPanels;
fprintf('panels: AutoResizeChildren %s, %d columns, left panel column %d, right panel column %d, label %s\n', ...
    app.UIFigure.AutoResizeChildren, numel(app.GridLayout.ColumnWidth), app.LeftPanel.Layout.Column, ...
    app.RightPanel.Layout.Column, app.StatusLabel.Text);
app.Button.ButtonPushedFcn(app.Button, []);
fprintf('panels: pushed -> %s\n', app.StatusLabel.Text);
app.UIFigure.Position = [100 100 500 480];
app.UIFigure.SizeChangedFcn(app.UIFigure, []);
fprintf('panels: narrow -> %d rows, %d columns, right panel row %d column %d\n', numel(app.GridLayout.RowHeight), ...
    numel(app.GridLayout.ColumnWidth), app.RightPanel.Layout.Row, app.RightPanel.Layout.Column);
delete(app);

% --- AcceptFormPlain: the same form in the plain-text format ----------------------------------------
try, feature('AppDesignerPlainTextFileFormat', 1); catch, end %#ok<CTCH> R2025b keeps the format behind the feature; JGraph has no feature
app = AcceptFormPlain(7);
app.ComputeButton.ButtonPushedFcn(app.ComputeButton, []);
fprintf('plain: class %s, field %g, add -> %s\n', class(app), app.EditField.Value, app.ResultLabel.Text);
delete(app);
fprintf('done\n');
