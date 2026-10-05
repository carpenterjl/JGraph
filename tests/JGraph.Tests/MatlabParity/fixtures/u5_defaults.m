% record: -noFigureWindows
% U5 of the app-building plan (ADR 0202): every uifigure component of the stage and uigridlayout
% as they are made - the names each answers to, the names it can be written by, its Type, and what
% every property starts as.
uf = uifigure('Visible', 'off', 'Position', [100 100 560 420]);
kinds = {'Label', 'Button', 'StateButton', 'EditField', 'NumericEditField', 'TextArea', 'DropDown', 'ListBox', 'CheckBox', ...
    'RadioButton', 'ToggleButton', 'Slider', 'RangeSlider', 'Spinner', 'Image', 'Hyperlink', 'GridLayout'};
% Left out: handles, which have no text; a slider's minor ticks and a grid's rectangles, which
% R2025b works out some time after the component is made (u5_grid waits for the rectangles).
skip = {'Parent', 'Children', 'ContextMenu', 'Layout', 'StyleConfigurations', 'UserData', 'MinorTicks'};
for k = 1:numel(kinds)
    kind = kinds{k};
    h = u5_make(kind, uf);
    names = sort(fieldnames(get(h)));
    fprintf('CHK|%s_type|%s|exact\n', kind, get(h, 'Type'));
    fprintf('CHK|%s_get_names|%s|exact\n', kind, strjoin(names', ' '));
    fprintf('CHK|%s_set_names|%s|exact\n', kind, strjoin(sort(fieldnames(set(h)))', ' '));
    for n = 1:numel(names)
        if ~any(strcmp(names{n}, skip)) && ~(strcmp(kind, 'GridLayout') && ~isempty(strfind(names{n}, 'Position')))
            fprintf('CHK|%s_default_%s|%s|exact\n', kind, names{n}, u5_text(get(h, names{n})));
        end
    end
    fprintf('CHK|%s_userdata_empty|%d|exact\n', kind, isempty(get(h, 'UserData')));
    u2_chk([kind '_layout_empty'], @() isempty(get(h, 'Layout')));
    fprintf('CHK|%s_parent_type|%s|exact\n', kind, get(get(h, 'Parent'), 'Type'));
    fprintf('CHK|%s_flags|%d %d %d %d %d|exact\n', kind, isprop(h, 'Units'), isgraphics(h), ishghandle(h), isvalid(h), ishandle(h));
    delete(allchild(uf));
end
delete(uf);
