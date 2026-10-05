% record: -noFigureWindows
% U8 of the app-building plan (ADR 0206): a table, a tab group, a tab, a menu, a context menu, a
% toolbar and its two tools as they are made, in a classic figure (F) and in a uifigure (U) - the
% names each answers to, the names it can be written by, its Type, and what every property starts
% as.
kinds = {'Table', 'TabGroup', 'Tab', 'Menu', 'SubMenu', 'ContextMenu', 'Toolbar', 'PushTool', 'ToggleTool'};
% Left out: handles, which have no text, and a table's StyleConfigurations, which is a table.
skip = {'Parent', 'Children', 'ContextMenu', 'Layout', 'StyleConfigurations', 'UserData', 'SelectedTab'};
for q = 1:2
    if q == 1
        p = figure('Visible', 'off', 'Position', [100 100 560 420]); w = 'F';
    else
        p = uifigure('Visible', 'off', 'Position', [100 100 560 420]); w = 'U';
    end
    for k = 1:numel(kinds)
        kind = kinds{k};
        h = u8_make(kind, p);
        label = [w '_' kind];
        names = sort(fieldnames(get(h)));
        fprintf('CHK|%s_type|%s|exact\n', label, get(h, 'Type'));
        fprintf('CHK|%s_get_names|%s|exact\n', label, strjoin(names', ' '));
        fprintf('CHK|%s_set_names|%s|exact\n', label, strjoin(sort(fieldnames(set(h)))', ' '));
        for n = 1:numel(names)
            % R2025b measures a tab, and what a tab group leaves its tabs, some time after they
            % are made, and in a classic figure only once a window shows them: until then it
            % answers the group's whole rectangle. u8_tabs waits for the settled sizes.
            unsettled = (strcmp(kind, 'Tab') && ~isempty(strfind(names{n}, 'Position'))) ...
                || (strcmp(kind, 'TabGroup') && strcmp(names{n}, 'InnerPosition'));
            if ~any(strcmp(names{n}, skip)) && ~unsettled
                fprintf('CHK|%s_default_%s|%s|exact\n', label, names{n}, u5_text(get(h, names{n})));
            end
        end
        fprintf('CHK|%s_userdata_empty|%d|exact\n', label, isempty(get(h, 'UserData')));
        fprintf('CHK|%s_children_empty|%d|exact\n', label, isempty(get(h, 'Children')));
        fprintf('CHK|%s_contextmenu_empty|%d|exact\n', label, isempty(get(h, 'ContextMenu')));
        fprintf('CHK|%s_parent_type|%s|exact\n', label, get(get(h, 'Parent'), 'Type'));
        fprintf('CHK|%s_flags|%d %d %d %d %d %d %d %d|exact\n', label, isprop(h, 'Units'), isprop(h, 'Position'), ...
            isprop(h, 'Visible'), isprop(h, 'Enable'), isgraphics(h), ishghandle(h), isvalid(h), ishandle(h));
        sv = set(h);
        words = fieldnames(sv);
        for n = 1:numel(words)
            if ~isempty(sv.(words{n}))
                fprintf('CHK|%s_words_%s|%s|exact\n', label, words{n}, u5_text(sv.(words{n})));
            end
        end
        delete(allchild(p));
    end
    delete(p);
end
