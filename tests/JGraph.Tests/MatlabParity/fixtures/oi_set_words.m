% Open item 41 (ADR 0220): set(h) of a figure, an axes, a plot or a decoration is a struct of the
% names that can be written, each holding the words the property takes, and set(h, name) is one
% name's words, as for a component since U3. The words are R2025b's, recorded with the names by
% tools/matlab-checklist/graphics-probes/graphics_class_names.m. Every figure stays invisible. Probe
% probe_41 (open-items scratch).

f = figure('Visible', 'off');
ax = axes(f);
hold(ax, 'on');
kinds = {
    'figure', f, {'Units', 'Visible', 'Pointer', 'WindowStyle', 'MenuBar', 'ToolBar', 'NumberTitle', 'PaperType', 'HandleVisibility', 'Resize', 'Name', 'Color'}
    'axes', ax, {'XScale', 'YDir', 'Box', 'GridLineStyle', 'TickDir', 'NextPlot', 'XAxisLocation', 'YAxisLocation', 'Layer', 'Projection', 'FontWeight', 'XLim', 'Color'}
    'line', plot(ax, 1:3), {'LineStyle', 'Marker', 'MarkerEdgeColor', 'MarkerFaceColor', 'LineJoin', 'Visible', 'XData', 'Color', 'LineWidth'}
    'text', text(ax, 1, 1, 'a'), {'Interpreter', 'HorizontalAlignment', 'VerticalAlignment', 'FontWeight', 'FontAngle', 'Units', 'String'}
    'patch', patch(ax, [0 1 1], [0 0 1], 'r'), {'FaceColor', 'EdgeColor', 'LineStyle', 'Marker', 'FaceLighting'}
    'surface', surface(ax, magic(3)), {'FaceColor', 'EdgeColor', 'LineStyle', 'MeshStyle', 'FaceLighting'}
    'scatter', scatter(ax, 1:3, 1:3), {'Marker', 'MarkerEdgeColor', 'MarkerFaceColor', 'LineWidth'}
    'bar', bar(ax, 1:3), {'BarLayout', 'Horizontal', 'FaceColor', 'EdgeColor', 'LineStyle', 'ShowBaseLine'}
    'legend', legend(ax), {'Location', 'Orientation', 'Box', 'Interpreter', 'TextColor'}
    };
for k = 1:size(kinds, 1)
    label = kinds{k, 1};
    h = kinds{k, 2};
    sv = set(h);
    fprintf('CHK|%s_set_class|%s|exact\n', label, class(sv));
    names = kinds{k, 3};
    for n = 1:numel(names)
        fprintf('CHK|%s_listed_%s|%d|exact\n', label, names{n}, isfield(sv, names{n}));
        if isfield(sv, names{n})
            fprintf('CHK|%s_words_%s|%s|exact\n', label, names{n}, u5_text(sv.(names{n})));
        end
    end
end
u9b_chk('one_name', @() set(ax, 'XScale'));
u9b_chk('one_name_no_words', @() set(ax, 'XLim'));
u9b_chk('one_name_read_only', @() set(ax, 'Type'));
u9b_chk('one_name_unknown', @() set(ax, 'Bogus'));
u9b_chk('listed_read_only', @() isfield(set(ax), 'Type'));
close all force;
