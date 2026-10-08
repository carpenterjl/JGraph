function ex8_drive
% Runs ex8_custom headless and drives what can be driven without a window: "Add 10" through its
% callback, a drawnow for the update, and what the component reports. Run in R2025b
% (run-probe.ps1) and in JGraph's command line; the two outputs are compared.
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, '..', 'research', 'apps', 'examples'));
[fig, s] = ex8_custom();
drawnow;
fprintf('after drawnow: Value %g, %d setup, %d updates, Type %s\n', s.Value, s.SetupCalls, s.UpdateCalls, s.Type);
add = findobj(fig, 'Tag', 'add');
for k = 1:3
    add.ButtonPushedFcn(add, []);
end
fprintf('after three presses: Value %g, %d updates before drawnow\n', s.Value, s.UpdateCalls);
drawnow;
fprintf('after drawnow: %d updates, label "%s"\n', s.UpdateCalls, get(findobj(fig, 'Tag', 'heard'), 'Text'));
fprintf('children of the figure: %d, types %s\n', numel(fig.Children), strjoin(sort(get(fig.Children, 'Type'))', ','));
delete(fig);
fprintf('component valid after the figure: %d\n', isvalid(s));
end
