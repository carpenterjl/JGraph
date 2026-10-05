% record: -noFigureWindows
% U8 of the app-building plan (ADR 0206): how a tab group and its tabs behave - which tab shows as
% tabs come and go, what a tab's rectangle is once R2025b has measured it, where things placed in a
% tab are, and the order of a group's children - in a classic figure (F) and a uifigure (U).
for q = 1:2
    if q == 1
        p = figure('Visible', 'off', 'Position', [100 100 560 420]); w = 'F';
    else
        p = uifigure('Visible', 'off', 'Position', [100 100 560 420]); w = 'U';
    end

    % which tab shows
    tg = uitabgroup(p);
    fprintf('CHK|%s_selected_none|%d|exact\n', w, isempty(tg.SelectedTab));
    t1 = uitab(tg, 'Title', 'One'); t2 = uitab(tg, 'Title', 'Two'); t3 = uitab(tg, 'Title', 'Three');
    fprintf('CHK|%s_selected_first|%s|exact\n', w, tg.SelectedTab.Title);
    fprintf('CHK|%s_children|%s|exact\n', w, u8_titles(tg));
    tg.SelectedTab = t2;
    fprintf('CHK|%s_selected_set|%s|exact\n', w, tg.SelectedTab.Title);
    u2_err([w '_select_number'], @() set(tg, 'SelectedTab', 3));
    u2_err([w '_select_nothing'], @() set(tg, 'SelectedTab', []));
    u2_err([w '_select_foreign'], @() set(tg, 'SelectedTab', uitab(uitabgroup(p, 'Position', tg.Position))));
    u2_err([w '_select_panel'], @() set(tg, 'SelectedTab', uipanel(p)));
    u2_err([w '_select_same'], @() set(tg, 'SelectedTab', t2));
    gone = uitab(tg, 'Title', 'gone'); delete(gone);
    u2_err([w '_select_deleted'], @() set(tg, 'SelectedTab', gone));
    u2_err([w '_select_in_maker'], @() uitabgroup(p, 'SelectedTab', t1));
    delete(setdiff(allchild(p), tg));
    delete(t2);
    fprintf('CHK|%s_after_middle|%s|exact\n', w, tg.SelectedTab.Title);
    t4 = uitab(tg, 'Title', 'Four');
    tg.SelectedTab = t4; delete(t4);
    fprintf('CHK|%s_after_last|%s|exact\n', w, tg.SelectedTab.Title);
    tg.SelectedTab = t1; delete(t1);
    fprintf('CHK|%s_after_first|%s|exact\n', w, tg.SelectedTab.Title);
    delete(t3);
    fprintf('CHK|%s_after_only|%d|exact\n', w, isempty(tg.SelectedTab));
    delete(tg);

    % order, and a tab moved to another group
    tg = uitabgroup(p); a = uitab(tg, 'Title', 'A'); b = uitab(tg, 'Title', 'B'); c = uitab(tg, 'Title', 'C');
    tg.SelectedTab = b;
    tg.Children = flipud(tg.Children(:));
    fprintf('CHK|%s_reordered|%s sel=%s|exact\n', w, u8_titles(tg), tg.SelectedTab.Title);
    uistack(a, 'top');
    fprintf('CHK|%s_restacked|%s|exact\n', w, u8_titles(tg));
    other = uitabgroup(p);
    c.Parent = other;
    fprintf('CHK|%s_moved|%s n=%d|exact\n', w, other.SelectedTab.Title, numel(other.Children));
    fprintf('CHK|%s_moved_from|%s sel=%s|exact\n', w, u8_titles(tg), tg.SelectedTab.Title);
    b.Parent = other;
    fprintf('CHK|%s_moved_selected|%s then %s|exact\n', w, tg.SelectedTab.Title, other.SelectedTab.Title);
    delete(allchild(p));

    % a script's choice of tab tells nobody
    tg = uitabgroup(p); a = uitab(tg); b = uitab(tg); %#ok<NASGU>
    setappdata(p, 'fired', 0);
    tg.SelectionChangedFcn = @(~, ~) setappdata(p, 'fired', getappdata(p, 'fired') + 1);
    tg.SelectedTab = b; drawnow;
    fprintf('CHK|%s_code_select_fires|%d|exact\n', w, getappdata(p, 'fired'));
    delete(allchild(p));

    % what a tab is
    tg = uitabgroup(p); t = uitab(tg);
    u2_chk([w '_title_cell'], @() get(uitab(tg, 'Title', {'a', 'b'}), 'Title'));
    u2_chk([w '_title_string'], @() get(uitab(tg, 'Title', "s"), 'Title'));
    u2_chk([w '_title_number'], @() get(uitab(tg, 'Title', 5), 'Title'));
    u2_chk([w '_title_matrix'], @() get(uitab(tg, 'Title', ['ab'; 'cd']), 'Title'));
    u2_err([w '_tab_set_position'], @() set(t, 'Position', [0 0 0.5 0.5]));
    u2_err([w '_tab_set_inner'], @() set(t, 'InnerPosition', [1 1 5 5]));
    u2_err([w '_tab_set_outer'], @() set(t, 'OuterPosition', [1 1 5 5]));
    u2_err([w '_group_set_inner'], @() set(tg, 'InnerPosition', [1 1 5 5]));
    u2_chk([w '_tab_visible'], @() get(t, 'Visible'));
    u2_chk([w '_tab_enable'], @() get(t, 'Enable'));
    u2_chk([w '_group_enable'], @() get(tg, 'Enable'));
    u2_chk([w '_group_tooltipstring'], @() get(uitabgroup(p, 'TooltipString', 'x'), 'Tooltip'));
    u2_chk([w '_tab_tooltipstring'], @() get(uitab(tg, 'TooltipString', 'x'), 'Tooltip'));
    u2_chk([w '_tab_none'], @() [get(uitab(tg, 'BackgroundColor', 'none'), 'BackgroundColor') ' ' get(uitab(tg, 'ForegroundColor', 'none'), 'ForegroundColor')]);
    u2_chk([w '_tab_scrollable'], @() get(uitab(tg, 'Scrollable', 'on'), 'Scrollable'));
    u2_chk([w '_group_hidden'], @() u8_hidden(p));
    u2_chk([w '_group_deleted'], @() u8_deleted(p));
    u2_chk([w '_findobj_tabs'], @() numel(findobj(tg, 'Type', 'uitab')));
    u2_chk([w '_ancestor'], @() get(ancestor(t, 'figure'), 'Type'));
    u2_chk([w '_group_callbacks'], @() u8_callbacks(uitabgroup(p), 'SelectionChangedFcn'));
    u2_chk([w '_group_outer'], @() u8_setget(uitabgroup(p, 'Units', 'pixels'), 'OuterPosition', [30 30 200 100], 'Position'));
    delete(allchild(p));

    % what is placed in a tab: its children newest first, an axes after the components
    tg = uitabgroup(p); t = uitab(tg);
    if q == 1, uicontrol(t); ax = axes(t); else, uibutton(t); ax = uiaxes(t); end
    uipanel(t); uitable(t);
    fprintf('CHK|%s_tab_children|%s|exact\n', w, u8_types(t.Children));
    fprintf('CHK|%s_axes_parent|%s|exact\n', w, get(get(ax, 'Parent'), 'Type'));
    fprintf('CHK|%s_axes_units|%s|exact\n', w, get(ax, 'Units'));
    fprintf('CHK|%s_nested_group|%s|exact\n', w, u8_chain(uitab(uitabgroup(t))));
    delete(allchild(p));

    % The rectangles, once R2025b has measured them. In a classic figure that is never shown it
    % never does, and goes on answering the group's own size for its tabs, so only a uifigure's
    % are asked for.
    if q == 2
        % The first layout of a uifigure takes R2025b longest: one group is made and left to
        % settle before any is measured.
        warm = uitabgroup(p); uitab(warm, 'Title', 'x');
        drawnow; pause(3);
        delete(warm);
        places = {'top', 'bottom'};
        for k = 1:numel(places)
            tg = uitabgroup(p, 'TabLocation', places{k}, 'Position', [20 20 300 200]);
            t = uitab(tg, 'Title', 'Alpha'); uitab(tg, 'Title', 'Beta');
            b = uibutton(t, 'Position', [10 10 50 20]);
            drawnow; pause(1.5);
            fprintf('CHK|U_%s_tab|%s|exact\n', places{k}, mat2str(t.Position));
            fprintf('CHK|U_%s_tab_inner|%s|exact\n', places{k}, mat2str(t.InnerPosition));
            fprintf('CHK|U_%s_tab_outer|%s|exact\n', places{k}, mat2str(t.OuterPosition));
            fprintf('CHK|U_%s_group_inner|%s|exact\n', places{k}, mat2str(tg.InnerPosition));
            fprintf('CHK|U_%s_group|%s|exact\n', places{k}, mat2str(tg.Position));
            fprintf('CHK|U_%s_child_in_figure|%s|exact\n', places{k}, mat2str(getpixelposition(b, true)));
            fprintf('CHK|U_%s_tab_in_figure|%s|exact\n', places{k}, mat2str(getpixelposition(t, true)));
            t.Units = 'normalized';
            fprintf('CHK|U_%s_tab_normalized|%s|exact\n', places{k}, mat2str(t.Position));
            delete(allchild(p));
        end
        sides = {'left', 'right'};
        for k = 1:numel(sides)
            tg = uitabgroup(p, 'TabLocation', sides{k}, 'Position', [20 20 300 200]);
            t = uitab(tg, 'Title', 'Alpha');
            drawnow; pause(1.5);
            fprintf('CHK|U_%s_tab|%s|exact\n', sides{k}, mat2str(t.Position));
            fprintf('CHK|U_%s_group_inner|%s|exact\n', sides{k}, mat2str(tg.InnerPosition));
            % A longer heading widens the strip. R2025b goes on answering the group's old inner
            % rectangle after that, so only the tab's is asked for.
            uitab(tg, 'Title', 'A much longer title than that');
            drawnow; pause(1.5);
            fprintf('CHK|U_%s_tab_long|%s|exact\n', sides{k}, mat2str(t.Position));
            delete(allchild(p));
        end
        tg = uitabgroup(p, 'Position', [20 20 300 200]);
        drawnow; pause(1.5);
        fprintf('CHK|U_empty_group_inner|%s|exact\n', mat2str(tg.InnerPosition));
        delete(allchild(p));
    end
    delete(p);
end
