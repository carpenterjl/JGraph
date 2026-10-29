function w3_examples
% W3: JGraph's example apps for the Start Page (examples/apps), run in R2025b headless with the
% same callbacks AppDesignerStartPageTests.TheExampleAppsRunAndRespond drives in JGraph, each
% answer printed as "name: value" so the two can be held to each other. Run with run-probe.ps1.
root = fullfile(fileparts(fileparts(fileparts(fileparts(fileparts(mfilename('fullpath')))))), 'examples', 'apps');
folders = dir(root);
for k = 1:numel(folders)
    if folders(k).isdir && folders(k).name(1) ~= '.'
        addpath(fullfile(root, folders(k).name));
    end
end
cleanup = onCleanup(@() delete(findall(groot, 'Type', 'figure')));

a = SavingsApp; a.YearsField.Value = 2; f = a.YearsField.ValueChangedFcn; f(a.YearsField, []);
say('savings.balance', a.BalanceLabel.Text);
say('savings.points', numel(a.UIAxes.Children(1).XData));
delete(a);

a = PulseApp; a.ShapeDropDown.Value = 'Triangle'; f = a.ShapeDropDown.ValueChangedFcn; f(a.ShapeDropDown, []);
say('pulse.max', max(a.UIAxes.Children(1).YData));
delete(a);

a = PartsTreeApp; n = a.Tree.Children(3).Children(1); a.Tree.SelectedNodes = n;
f = a.Tree.SelectionChangedFcn; f(a.Tree, struct('SelectedNodes', n));
say('tree.details', [a.NameValue.Text ' ' a.CategoryValue.Text ' ' num2str(a.QuantityField.Value)]);
n.Text = 'Bearing 608-2RS'; g = a.Tree.NodeTextChangedFcn; g(a.Tree, struct('Node', n, 'PreviousText', 'Bearing 608'));
say('tree.renamed', n.NodeData.Name);
delete(a);

a = ImageApp; a.ChannelDropDown.Value = 'Gray'; f = a.ChannelDropDown.ValueChangedFcn; f(a.ChannelDropDown, []);
say('image.pixels', sum(a.HistogramAxes.Children(1).Values));
delete(a);

a = TimerApp; f = a.StartButton.ButtonPushedFcn; f(a.StartButton, []);
say('timer.started', [char(a.StopButton.Enable) ' ' char(a.StartButton.Enable)]);
g = a.StopButton.ButtonPushedFcn; g(a.StopButton, []);
say('timer.stopped', [char(a.StopButton.Enable) ' ' char(a.StartButton.Enable)]);
close(a.UIFigure);
say('timer.closed', sprintf('%d %d', isvalid(a), numel(timerfindall)));

a = PolarApp; a.LobesSpinner.Value = 6; f = a.LobesSpinner.ValueChangedFcn; f(a.LobesSpinner, []);
say('polar.type', a.PlotPanel.Children(1).Type);
delete(a);

a = TableApp; f = a.UITable.CellEditCallback; a.UITable.Data{1, 2} = -1;
f(a.UITable, struct('Indices', [1 2], 'NewData', -1, 'PreviousData', 0.5));
say('table.price', a.UITable.Data.Price(1));
g = a.AddButton.ButtonPushedFcn; g(a.AddButton, []);
say('table.total', a.TotalLabel.Text);
delete(a);

a = MainApp; f = a.OptionsButton.ButtonPushedFcn; f(a.OptionsButton, []);
say('multi.waiting', char(a.OptionsButton.Enable));
d = get(findall(groot, 'Type', 'figure', 'Name', 'Options'), 'RunningAppInstance');
d.ColorDropDown.Value = 'Black'; d.WidthSpinner.Value = 3; g = d.OKButton.ButtonPushedFcn; g(d.OKButton, []);
say('multi.result', sprintf('%g %g %g %g %s', a.UIAxes.Children(1).Color, a.UIAxes.Children(1).LineWidth, char(a.OptionsButton.Enable)));
say('multi.dialog', isvalid(d));
close(a.UIFigure);
end

function say(name, value)
if isnumeric(value) || islogical(value)
    value = num2str(value);
end
fprintf('%s: %s\n', name, value);
end
