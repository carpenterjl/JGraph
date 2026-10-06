% record: -noFigureWindows
% U9 of the app-building plan (ADR 0207): the knobs, switches, gauges, the lamp, the two pickers,
% the trees and a tree node as they are made, in a classic figure (F) and in a uifigure (U) - the
% names each answers to, the names it can be written by, its Type, and what every property starts
% as.
kinds = {'Knob', 'DiscreteKnob', 'Switch', 'RockerSwitch', 'ToggleSwitch', 'Gauge', 'LinearGauge', 'NinetyDegreeGauge', ...
    'SemicircularGauge', 'Lamp', 'DatePicker', 'ColorPicker', 'Tree', 'CheckBoxTree', 'TreeNode'};
% Left out: handles, which have no text; a tree's style table; the minor ticks and the outer
% rectangle of a dial or a switch, which R2025b works out some time after the component is made.
skip = {'Parent', 'Children', 'ContextMenu', 'Layout', 'StyleConfigurations', 'UserData', 'SelectedNodes', 'CheckedNodes', 'MinorTicks'};
outer = {'Knob', 'DiscreteKnob', 'Switch', 'RockerSwitch', 'ToggleSwitch'};
for q = 1:2
    if q == 1
        p = figure('Visible', 'off', 'Position', [100 100 560 420]); w = 'F';
    else
        p = uifigure('Visible', 'off', 'Position', [100 100 560 420]); w = 'U';
    end
    for k = 1:numel(kinds)
        kind = kinds{k};
        h = u9_make(kind, p);
        label = [w '_' kind];
        names = sort(fieldnames(get(h)));
        fprintf('CHK|%s_type|%s|exact\n', label, get(h, 'Type'));
        fprintf('CHK|%s_get_names|%s|exact\n', label, strjoin(names', ' '));
        fprintf('CHK|%s_set_names|%s|exact\n', label, strjoin(sort(fieldnames(set(h)))', ' '));
        for n = 1:numel(names)
            unsettled = any(strcmp(kind, outer)) && strcmp(names{n}, 'OuterPosition');
            if ~any(strcmp(names{n}, skip)) && ~unsettled
                fprintf('CHK|%s_default_%s|%s|exact\n', label, names{n}, u9_text(get(h, names{n})));
            end
        end
        fprintf('CHK|%s_userdata_empty|%d|exact\n', label, isempty(get(h, 'UserData')));
        fprintf('CHK|%s_children_prop|%d|exact\n', label, isprop(h, 'Children'));
        if isprop(h, 'Children')
            fprintf('CHK|%s_children_empty|%d|exact\n', label, isempty(get(h, 'Children')));
        end
        fprintf('CHK|%s_contextmenu_empty|%d|exact\n', label, isempty(get(h, 'ContextMenu')));
        fprintf('CHK|%s_parent_type|%s|exact\n', label, get(get(h, 'Parent'), 'Type'));
        fprintf('CHK|%s_flags|%d %d %d %d %d %d %d %d|exact\n', label, isprop(h, 'Units'), isprop(h, 'Position'), ...
            isprop(h, 'Visible'), isprop(h, 'Enable'), isgraphics(h), ishghandle(h), isvalid(h), ishandle(h));
        sv = set(h);
        words = fieldnames(sv);
        for n = 1:numel(words)
            if ~isempty(sv.(words{n}))
                fprintf('CHK|%s_words_%s|%s|exact\n', label, words{n}, u9_text(sv.(words{n})));
            end
        end
        delete(allchild(p));
    end
    delete(p);
end
