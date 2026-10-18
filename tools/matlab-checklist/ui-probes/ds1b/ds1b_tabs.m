% DS1b (designer plan): a tab's size against its tab group's, made in code headless: the group's size
% when the tab is made, whatever the tab location or font, and not worked out again.
for loc = {'top', 'bottom', 'left', 'right'}
    for pos = {[10 10 250 210], [5 5 672 488], [0 0 400 300]}
        for fs = [12 20]
            fig = uifigure('Visible', 'off'); g = uitabgroup(fig, 'Position', pos{1}, 'TabLocation', loc{1});
            t = uitab(g, 'Title', 'A'); t2 = uitab(g, 'Title', 'B'); %#ok<NASGU>
            g.FontSize = fs;
            fprintf('%-6s %-18s fs %2d  tab %s\n', loc{1}, mat2str(g.Position), fs, mat2str(t.Position));
            delete(fig);
        end
    end
end
% the order of setting: position after tabs
fig = uifigure('Visible', 'off'); g = uitabgroup(fig); t = uitab(g); g.Position = [5 5 672 488];
fprintf('position set after the tab: tab %s\n', mat2str(t.Position));
g2 = uitabgroup(uigridlayout(fig)); t2 = uitab(g2); fprintf('in a grid: group %s tab %s\n', mat2str(g2.Position), mat2str(t2.Position));
