function ex6_dashboard
% EX6_DASHBOARD  A plant dashboard written as a function with nested callbacks (app-building
% stage U9): gauges and lamps that follow a knob, a switch that arms the alarms, a tree of the
% plant's units whose selection drives the readouts, a date picker for the shift and a colour
% picker for the trend line. Every component is tagged, so a window check can find it by its tag.
% Nested functions, not local ones: a script's local functions cannot see its variables.

fig = uifigure('Name', 'Plant dashboard', 'Position', [100 100 820 520], 'Tag', 'dash');
gl = uigridlayout(fig, [3 4]);
gl.RowHeight = {'fit', '1x', 'fit'};
gl.ColumnWidth = {200, '1x', '1x', '1x'};

% --- the plant's units, as a tree with check boxes --------------------------------------------
tree = uitree(gl, 'checkbox', 'Tag', 'units');
tree.Layout.Row = [1 2];
tree.Layout.Column = 1;
site = uitreenode(tree, 'Text', 'North site', 'Tag', 'north');
uitreenode(site, 'Text', 'Boiler 1', 'NodeData', 72, 'Tag', 'boiler1');
uitreenode(site, 'Text', 'Boiler 2', 'NodeData', 48, 'Tag', 'boiler2');
south = uitreenode(tree, 'Text', 'South site', 'Tag', 'south');
uitreenode(south, 'Text', 'Turbine', 'NodeData', 91, 'Tag', 'turbine');
expand(tree, 'all');
addStyle(tree, uistyle('FontWeight', 'bold'), 'level', 1);

% --- the readouts ----------------------------------------------------------------------------
pressure = uigauge(gl, 'Tag', 'pressure', 'Limits', [0 100], 'Value', 40, ...
    'ScaleColors', {'green', 'yellow', 'red'}, 'ScaleColorLimits', [0 60; 60 85; 85 100]);
pressure.Layout.Row = 1;
pressure.Layout.Column = 2;
flow = uigauge(gl, 'semicircular', 'Tag', 'flow', 'Limits', [0 10], 'Value', 4);
flow.Layout.Row = 1;
flow.Layout.Column = 3;
level = uigauge(gl, 'linear', 'Tag', 'level', 'Limits', [0 100], 'Value', 40, 'ScaleColors', [0.2 0.6 1]);
level.Layout.Row = 1;
level.Layout.Column = 4;

trend = uiaxes(gl, 'Tag', 'trend');
trend.Layout.Row = 2;
trend.Layout.Column = [2 4];
title(trend, 'Pressure trend');
history = plot(trend, 1:12, 40 + 5 * sin((1:12) / 2), 'LineWidth', 2, 'Color', [0 0.45 0.74]);
ylim(trend, [0 100]);

% --- the controls ----------------------------------------------------------------------------
controls = uigridlayout(gl, [1 7]);
controls.Layout.Row = 3;
controls.Layout.Column = [1 4];
controls.ColumnWidth = {'fit', 'fit', 'fit', 40, 150, 60, '1x'};
controls.RowHeight = {'fit'};
knob = uiknob(controls, 'Tag', 'setpoint', 'Limits', [0 100], 'Value', 40);
arm = uiswitch(controls, 'Tag', 'arm', 'Items', {'Off', 'Armed'});
mode = uiknob(controls, 'discrete', 'Tag', 'mode', 'Items', {'Idle', 'Run', 'Purge'});
alarm = uilamp(controls, 'Tag', 'alarm', 'Color', [0.5 0.5 0.5]);
shift = uidatepicker(controls, 'Tag', 'shift', 'Value', datetime(2026, 10, 5), 'DisplayFormat', 'dd/MM/uuuu');
ink = uicolorpicker(controls, 'Tag', 'ink', 'Value', [0 0.45 0.74]);
status = uilabel(controls, 'Tag', 'status', 'Text', 'Ready');

% --- the wiring ------------------------------------------------------------------------------
knob.ValueChangingFcn = @(src, e) follow(e.Value);
knob.ValueChangedFcn = @(src, e) follow(e.Value);
arm.ValueChangedFcn = @(src, e) alarms();
mode.ValueChangedFcn = @(src, e) say(sprintf('Mode %s', e.Value));
tree.SelectionChangedFcn = @(src, e) pick(e.SelectedNodes);
tree.CheckedNodesChangedFcn = @(src, e) say(sprintf('%d units checked', numel(e.LeafCheckedNodes)));
shift.ValueChangedFcn = @(src, e) say(sprintf('Shift %s', char(string(e.Value))));
ink.ValueChangedFcn = @(src, e) recolour(e.Value);
menu = uicontextmenu(fig);
uimenu(menu, 'Text', 'Reset setpoint', 'MenuSelectedFcn', @(src, e) reset());
knob.ContextMenu = menu;
pressure.ContextMenu = menu;
say('Ready');

    function follow(setpoint)
        pressure.Value = setpoint;
        level.Value = setpoint;
        flow.Value = setpoint / 10;
        alarms();
    end

    function alarms()
        armed = strcmp(arm.Value, 'Armed');
        if armed && pressure.Value > 85
            alarm.Color = 'red';
            say(sprintf('ALARM pressure %.0f', pressure.Value));
        elseif armed
            alarm.Color = 'green';
            say(sprintf('Armed, pressure %.0f', pressure.Value));
        else
            alarm.Color = [0.5 0.5 0.5];
            say(sprintf('Disarmed, pressure %.0f', pressure.Value));
        end
    end

    function pick(nodes)
        if isempty(nodes)
            return;
        end
        node = nodes(1);
        if isempty(node.NodeData)
            say(sprintf('Site %s', node.Text));
        else
            knob.Value = node.NodeData;
            follow(node.NodeData);
            say(sprintf('%s at %d', node.Text, node.NodeData));
        end
    end

    function recolour(rgb)
        history.Color = rgb;
        say(sprintf('Trend colour %s', mat2str(rgb, 2)));
    end

    function reset()
        knob.Value = 40;
        follow(40);
        say('Setpoint reset');
    end

    function say(text)
        status.Text = text;
        fprintf('%s\n', text);
    end
end
