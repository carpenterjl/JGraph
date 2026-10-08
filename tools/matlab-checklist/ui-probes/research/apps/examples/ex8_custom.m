function [fig, s] = ex8_custom
% EX8_CUSTOM  A custom component in an app (app-building stage U10): research C's SpinnerGauge, a
% spinner and a linear gauge kept in step by the component's update. "Add 10" writes the
% component's Value, which its update shows in both; turning the spinner runs the component's
% ValueChangedFcn, which the label below reports. Every app component is tagged, so a window
% check can find it by its tag.

fig = uifigure('Name', 'Custom component', 'Position', [100 100 360 240], 'Tag', 'ex8');
s = SpinnerGauge(fig, 'Value', 40, 'Limits', [0 100]);
s.Position = [20 110 320 110];

heard = uilabel(fig, 'Position', [20 70 320 22], 'Text', 'Nothing heard yet', 'Tag', 'heard');
uibutton(fig, 'Text', 'Add 10', 'Position', [20 30 100 26], 'Tag', 'add', ...
    'ButtonPushedFcn', @(~, ~) set(s, 'Value', min(s.Value + 10, s.Limits(2))));
s.ValueChangedFcn = @(src, event) set(heard, 'Text', sprintf('%s: %g', event.EventName, src.Value));
fprintf('ex8_custom ready: Value %g, %d setup, %d updates\n', s.Value, s.SetupCalls, s.UpdateCalls);
end
