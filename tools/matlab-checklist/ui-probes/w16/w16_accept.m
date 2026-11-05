% w16_accept - the App Designer window plan's acceptance apps (W16), begun as the Start Page's tiles begin
% them and built by the local test AppDesignerWindowAcceptanceTests with JGRAPH_W16_OUT naming this folder's
% rt (rt\accept: W16Blank.mlapp from Blank App, W16Panels.mlapp from 2-Panel App with Auto-Reflow,
% W16Counter.mlapp from Blank Component with a grid, registered in rt\accept\resources\appDesigner.json as
% Configure for Apps registers one, and W16Composite.mlapp, a blank app using the Counter from the library),
% run in R2025b's -batch and in JGraph's, the two outputs held to each other:
%   powershell -ExecutionPolicy Bypass -File .\run-probe.ps1 -Name w16_accept
%   ..\..\..\..\src\JGraph.Cli\bin\Debug\net8.0\jgraph.exe -batch w16_accept.m > w16_accept.jgraph.out
% Each app is constructed, its callbacks called as its window would call them, its state printed and the
% app deleted, so neither engine has a window left to note. Nothing here is engine-specific.
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, 'rt', 'accept'));

% --- W16Blank: a numeric field, a slider, a button, a label and an axes the startup function plots into --
app = W16Blank;
fprintf('blank: figure %s, field %g, slider %g in %s, label %s, %d line(s), last y %g\n', app.W16BlankUIFigure.Name, ...
    app.EditField.Value, app.Slider.Value, mat2str(app.Slider.Limits), app.ResultLabel.Text, ...
    numel(app.UIAxes.Children), app.UIAxes.Children(1).YData(end));
app.ScaleButton.ButtonPushedFcn(app.ScaleButton, []);
fprintf('blank: scale -> %s\n', app.ResultLabel.Text);
app.EditField.Value = 1.5;
app.Slider.Value = 7;
app.ScaleButton.ButtonPushedFcn(app.ScaleButton, []);
fprintf('blank: 1.5 x 7 -> %s\n', app.ResultLabel.Text);
delete(app);

% --- W16Panels: the two-panel responsive template, a list box left and a label right ----------------------
app = W16Panels;
fprintf('panels: AutoResizeChildren %s, %d columns, items %s, value %s, label %s\n', app.UIFigure.AutoResizeChildren, ...
    numel(app.GridLayout.ColumnWidth), strjoin(app.ListBox.Items, ','), app.ListBox.Value, app.ChoiceLabel.Text);
app.ListBox.Value = 'Blue';
app.ListBox.ValueChangedFcn(app.ListBox, struct('Value', 'Blue'));
fprintf('panels: chose -> %s\n', app.ChoiceLabel.Text);
app.UIFigure.Position = [100 100 500 480];
app.UIFigure.SizeChangedFcn(app.UIFigure, []);
fprintf('panels: narrow -> %d rows, %d columns, right panel row %d column %d\n', numel(app.GridLayout.RowHeight), ...
    numel(app.GridLayout.ColumnWidth), app.RightPanel.Layout.Row, app.RightPanel.Layout.Column);
delete(app);

% --- W16Counter alone: the component in a figure, its managed properties, its public callback -------------
fig = uifigure('Visible', 'off');
c = W16Counter('Parent', fig);
fprintf('counter: class %s, Position %s, Count %g, Step %g, has CountedFcn %d, empty %d\n', class(c), mat2str(c.Position), ...
    c.Count, c.Step, isprop(c, 'CountedFcn'), isempty(c.CountedFcn));
try
    c.Step = 0;
    fprintf('counter: Step 0 taken\n');
catch e
    fprintf('counter: Step 0 refused: %s\n', e.message);
end
c.Step = 2;
c.Count = 3;
drawnow;
c.CountedFcn = @(s, e) fprintf('counter: CountedFcn called, Count %g, Step %g\n', s.Count, s.Step);
c.CountedFcn(c, []);
fprintf('counter: the button inside is private: %d found by findall\n', numel(findall(fig, 'Type', 'uibutton')));
delete(fig);

% --- A property typed with the component's class and no default, as the composite app declares its own ----
t = W16Typed;
fprintf('typed: Comp is %s %s, Num is %s %s, Words is %s %s\n', class(t.Comp), mat2str(size(t.Comp)), ...
    class(t.Num), mat2str(size(t.Num)), class(t.Words), mat2str(size(t.Words)));
delete(t);

% --- W16Composite: the app using the component from the library ------------------------------------------
app = W16Composite;
fprintf('composite: figure %s, component %s, Step %g, Count %g, label %s, %d children\n', app.W16CompositeUIFigure.Name, ...
    class(app.W16Counter), app.W16Counter.Step, app.W16Counter.Count, app.TotalLabel.Text, numel(app.W16CompositeUIFigure.Children));
app.W16Counter.Count = 10;
app.W16Counter.CountedFcn(app.W16Counter, []);
fprintf('composite: the public callback -> label %s, Count %g\n', app.TotalLabel.Text, app.W16Counter.Count);
delete(app);
fprintf('done\n');
