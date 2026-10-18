% record: -noFigureWindows
% Open items 140, 141 and 142 (ADR 0242). 140: each engine's own savefig and openfig keep the Tag
% of a text, a rectangle, a light, a legend, a colour bar and every annotation, and a rectangle stays
% one (probe oi140_tags). 141: a legend's Title is a text object of its own with R2025b's 21
% properties, following the legend's font and colour until written, bold until written (probes
% oi141_title, oi141_font, oi141_follow). 142: imshow sets up its axes as R2025b's base-MATLAB
% imshow.m does (probes oi142_imshow, oi142_errors). A fresh legend title's first read is stale in
% R2025b (oi141_font F1), so the title is touched and drawn before any read.

% --- 140: tags and kinds through each engine's own file --------------------------------------------
f = figure('Visible', 'off', 'Tag', 'fig');
ax = axes(f, 'Tag', 'ax');
hold(ax, 'on');
set(plot(ax, 1:3), 'Tag', 'line', 'UserData', 11);
text(ax, 1, 2, 'note', 'Tag', 'text');
set(rectangle(ax, 'Position', [0.5 0.5 1 1], 'Curvature', [0.3 0.6]), 'Tag', 'rect');
set(light(ax), 'Tag', 'light');
legend(ax, 'Tag', 'legend');
colorbar(ax, 'Tag', 'cbar');
annotation(f, 'textbox', [0.1 0.1 0.2 0.1], 'String', 'tb', 'Tag', 'a_textbox');
annotation(f, 'rectangle', [0.3 0.3 0.2 0.2], 'Tag', 'a_rectangle');
annotation(f, 'ellipse', [0.5 0.5 0.2 0.2], 'Tag', 'a_ellipse');
annotation(f, 'line', [0.1 0.4], [0.9 0.8], 'Tag', 'a_line');
annotation(f, 'arrow', [0.1 0.4], [0.7 0.6], 'Tag', 'a_arrow');
annotation(f, 'doublearrow', [0.1 0.4], [0.5 0.4], 'Tag', 'a_doublearrow');
annotation(f, 'textarrow', [0.1 0.4], [0.3 0.2], 'String', 'ta', 'Tag', 'a_textarrow');
drawn = {'fig', 'ax', 'line', 'text', 'rect', 'light', 'legend', 'cbar'};
shapes = {'a_textbox', 'a_rectangle', 'a_ellipse', 'a_line', 'a_arrow', 'a_doublearrow', 'a_textarrow'};
u2_chk('tag_types_before', @() types(f, drawn));
u2_chk('tag_shapes_before', @() counts(f, shapes, @findall));
tmp = [tempname '.fig'];
savefig(f, tmp);
close(f);
h = openfig(tmp, 'invisible');
u2_chk('tag_types_after', @() types(h, drawn));
u2_chk('tag_shapes_after', @() counts(h, shapes, @findall));
u9b_chkdiv('tag_shape_types_after', @() types(h, shapes), '0242'); % an annotation's Type is its kind here, R2025b's says shape (open item 143)
u9b_chkdiv('tag_shapes_findobj', @() counts(h, shapes, @findobj), '0242'); % findobj reaches the annotations here; R2025b's keeps them for findall (open item 143)
r = findobj(h, 'Tag', 'rect');
u2_chk('tag_rect_after', @() {r.Type, r.Position, r.Curvature});
u2_chk('tag_text_after', @() get(findobj(h, 'Tag', 'text'), 'String'));
u9b_chkdiv('tag_userdata_after', @() get(findobj(h, 'Tag', 'line'), 'UserData'), '0217'); % UserData does not travel through this build's figure files
delete(h);
delete(tmp);

f = figure('Visible', 'off');
rectangle('Position', [1 2 3 4], 'Curvature', 0.5, 'FaceColor', [1 1 0]);
tmp = [tempname '.fig'];
savefig(f, tmp);
close(f);
h = openfig(tmp, 'invisible');
r = findobj(h, 'Type', 'rectangle');
u2_chk('tag_rect_scalar', @() {numel(r), r.Position, r.Curvature, r.FaceColor});
delete(h);
delete(tmp);

% --- 141: the legend's title object ----------------------------------------------------------------------
[f, lg] = legend_figure();
t = lg.Title;
u2_chk('lt_isa', @() {isa(t, 'matlab.graphics.illustration.legend.Text'), isa(t, 'matlab.graphics.Graphics'), isa(t, 'matlab.graphics.primitive.Text'), isgraphics(t), isvalid(t)});
u9b_chkdiv('lt_class', @() class(t), '0051'); % a handle is a number here
u2_chk('lt_properties', @() properties(t)');
u2_chk('lt_get_fields', @() fieldnames(get(t))');
u2_chk('lt_defaults', @() {t.String, t.FontWeight, t.FontAngle, t.Interpreter, t.FontSize == lg.FontSize, isequal(t.Color, lg.TextColor), strcmp(t.FontName, lg.FontName)});
u2_chk('lt_modes', @() {t.ColorMode, t.FontAngleMode, t.FontNameMode, t.FontSizeMode, t.FontWeightMode, t.InterpreterMode});
u2_chk('lt_switches', @() {char(t.Visible), char(t.HitTest), t.PickableParts, t.HandleVisibility, char(t.Selected), char(t.SelectionHighlight)});
u2_chk('lt_parent', @() {isequal(t.Parent, lg), isempty(t.Children), isequal(get(lg, 'Title'), t)});
u2_chk('lt_title_returns', @() isequal(title(lg, 'Series'), lg.Title));
u2_chk('lt_string', @() lg.Title.String);
title(lg, 'S', 'FontWeight', 'normal', 'Color', 'r');
u2_chk('lt_options', @() {lg.Title.String, lg.Title.FontWeight, lg.Title.FontWeightMode, lg.Title.Color, lg.Title.ColorMode});
title(lg, {'a', 'b'});
u2_chk('lt_cell', @() lg.Title.String);
title(lg, ["a" "b"]);
u2_chk('lt_strings', @() lg.Title.String);
title(lg, 5);
u2_chk('lt_number', @() lg.Title.String);
lg.Title.String = "s";
u2_chk('lt_string_scalar', @() lg.Title.String);
lg.Title.FontWeightMode = 'auto';
lg.Title.ColorMode = 'auto';
lg.FontSize = 14;
drawnow;
u2_chk('lt_follow_size', @() {lg.Title.FontSize, lg.Title.FontSizeMode});
lg.FontName = 'Arial';
lg.Interpreter = 'latex';
lg.FontAngle = 'italic';
lg.TextColor = [1 0 0];
drawnow;
u2_chk('lt_follow_rest', @() {lg.Title.FontName, lg.Title.Interpreter, lg.Title.FontAngle, lg.Title.Color, lg.Title.FontWeight});
lg.Title.FontSize = 20;
lg.FontSize = 8;
drawnow;
u2_chk('lt_manual_size', @() {lg.Title.FontSize, lg.Title.FontSizeMode});
lg.Title.Color = 'none';
u2_chk('lt_none', @() {lg.Title.Color, lg.Title.ColorMode});
lg.Title.ColorMode = 'auto';
drawnow;
u2_chk('lt_color_auto', @() isequal(lg.Title.Color, lg.TextColor));
set(lg.Title, 'FontS', 12);
u2_chk('lt_partial', @() lg.Title.FontSize);
u2_chk('lt_set_words', @() {set(lg.Title, 'FontWeight')', set(lg.Title, 'Interpreter')', set(lg.Title, 'PickableParts')'});
u2_err('lt_err_type', @() get(lg.Title, 'Type'));
u2_err('lt_err_position', @() get(lg.Title, 'Position'));
u2_err('lt_err_bogus', @() set(lg.Title, 'Bogus', 1));
u2_err('lt_err_size', @() set(lg.Title, 'FontSize', 0));
u2_err('lt_err_weight', @() set(lg.Title, 'FontWeight', 'heavy'));
u2_err('lt_err_parent', @() set(lg.Title, 'Parent', gca));
u2_err('lt_err_title', @() title(lg));
u9b_chkdiv('lt_assign', @() attempt(@() assign_title(lg)), '0242'); % this build titles the legend; R2025b refuses the write as read-only
close(f);

[f, lg] = legend_figure();
lg.Title.String = 'Kept';
delete(lg.Title);
u2_chk('lt_deleted', @() {isvalid(lg.Title), lg.Title.String});
lg.Title.String = 'Kept';
lg2 = legend('again');
u2_chk('lt_legend_again', @() {isequal(lg, lg2), lg2.Title.String});
t = lg.Title;
legend('off');
lg3 = legend('third');
u2_chk('lt_legend_remade', @() {isvalid(t), lg3.Title.String});
close(f);

[f, lg] = legend_figure();
title(lg, 'Saved', 'FontWeight', 'normal');
tmp = [tempname '.fig'];
savefig(f, tmp);
close(f);
h = openfig(tmp, 'invisible');
lgr = findobj(h, 'Type', 'legend');
u2_chk('lt_saved', @() {lgr.Title.String, lgr.Title.FontWeight});
delete(h);
delete(tmp);

% --- 142: imshow's axes over the forms --------------------------------------------------------------
show('is_double_auto', @() imshow(magic(4), []));
show('is_double', @() imshow(reshape(1:20, 4, 5) / 20));
show('is_uint8', @() imshow(uint8(magic(4) * 10)));
show('is_uint16', @() imshow(uint16(magic(4) * 1000)));
show('is_int16', @() imshow(int16(magic(4) * 100 - 800)));
show('is_int8', @() imshow(int8(magic(4) - 8)));
show('is_logical', @() imshow(logical(eye(4))));
show('is_single', @() imshow(single(magic(4) / 16)));
show('is_rgb', @() imshow(reshape(1:48, 4, 4, 3) / 48));
show('is_rgb_uint8', @() imshow(uint8(reshape(1:48, 4, 4, 3) * 5)));
show('is_rgb_auto', @() imshow(reshape(1:48, 4, 4, 3) / 48, []));
show('is_rgb_clip', @() imshow(reshape(-3:44, 4, 4, 3) / 40));
show('is_indexed', @() imshow(magic(4) / 2, eight()));
show('is_indexed_uint8', @() imshow(uint8(mod(magic(4), 8)), eight()));
show('is_rgb_map', @() imshow(reshape(1:48, 4, 4, 3) / 48, eight()));
show('is_range', @() imshow(magic(4) / 16, [0.2 0.8]));
show('is_range_named', @() imshow(magic(4) / 16, 'DisplayRange', [0.2 0.8]));
show('is_range_abbrev', @() imshow(magic(4) / 16, 'disp', [0 2]));
show('is_range_empty', @() imshow(magic(4) / 4, 'DisplayRange', []));
show('is_xydata', @() imshow(magic(4) / 16, 'XData', [10 20], 'YData', [5 8]));
show('is_xdata', @() imshow(magic(4) / 16, 'XData', [10 20]));
show('is_colormap', @() imshow(magic(4) / 16, 'Colormap', [eight(); flipud(eight())]));
show('is_bilinear', @() imshow(magic(4) / 16, 'Interpolation', 'bilinear'));
show('is_constant', @() imshow(ones(3) * 5, []));
show('is_int16_auto', @() imshow(int16(magic(4) * 100 - 800), []));
show('is_empty', @() imshow(zeros(0, 3)));
show('is_held', @() then(@() plot(1:10), @() held_imshow()));
show('is_replaced', @() then(@() plot(1:10), @() imshow(magic(4) / 16)));
show('is_subplot', @() then(@() subplot(1, 2, 1), @() imshow(magic(4) / 16)));
show('is_fit', @() imshow(magic(4) / 16, 'InitialMagnification', 'fit'));
show('is_fit_tight', @() imshow(magic(4) / 16, 'Border', 'tight', 'InitialMagnification', 'fit'));
show('is_twice', @() then(@() imshow(magic(4) / 16), @() imshow(magic(4) / 32)));
place('is_place_subplot', @() then(@() subplot(1, 2, 1), @() imshow(magic(4) / 16)));
place('is_place_fit', @() imshow(magic(4) / 16, 'InitialMagnification', 'fit'));
place('is_place_fit_tight', @() imshow(magic(4) / 16, 'Border', 'tight', 'InitialMagnification', 'fit'));
f = figure('Visible', 'off');
imshow(magic(4) / 16);
u9b_chkdiv('is_place_alone', @() {f.Position(3:4), round(get(gca, 'Position'), 4)}, '0242'); % R2025b sizes the figure and the axes to the picture at 100%
close(f);
f = figure('Visible', 'off');
imshow(reshape(1:48, 4, 4, 3) / 48, eight());
u2_chk('is_rgb_map_colormap', @() {size(get(gca, 'Colormap')), round(get(gca, 'Colormap'), 2)});
close(f);

f = figure('Visible', 'off');
a1 = subplot(1, 2, 1);
a2 = subplot(1, 2, 2);
plot(1:3);
h = imshow(magic(4) / 16, 'Parent', a1);
u2_chk('is_parent', @() {isequal(gca, a2), a1.XLim, a1.XLimMode, char(a1.Visible), char(a2.Visible), h.Type});
close(f);

f = figure('Visible', 'off');
h = imshow(magic(4) / 16 + 1i);
[msg, id] = lastwarn;
u2_chk('is_complex', @() {id, msg, isreal(h.CData)});
close(f);
f = figure('Visible', 'off');
imshow(reshape(1:48, 4, 4, 3) / 48, 'Colormap', eight());
[msg, id] = lastwarn;
u2_chk('is_rgb_colormap', @() {id, msg, get(gca, 'CLimMode')});
close(f);

f = figure('Visible', 'off');
imshow(magic(4) / 16);
ax0 = gca;
u2_chk('is_nextplot', @() get(f, 'NextPlot'));
plot(1:3);
ax = gca;
u2_chk('is_then_plot', @() {isequal(ax, ax0), char(ax.Visible), ax.YDir, ax.DataAspectRatioMode, get(f, 'NextPlot'), numel(findobj(f, 'Type', 'axes')), ax.XLimMode});
close(f);

f = figure('Visible', 'off');
subplot(1, 2, 1);
imshow(magic(4) / 16);
ax = gca;
plot(ax, 1:3);
u2_chk('is_subplot_then_plot', @() {char(ax.Visible), ax.YDir, ax.TickDirMode, ax.DataAspectRatioMode, ax.Layer, ax.XLimMode});
close(f);

f = figure('Visible', 'off');
set(f, 'NextPlot', 'replacechildren');
plot(1:3);
u2_chk('is_figure_nextplot_once', @() get(f, 'NextPlot'));
subplot(2, 1, 1);
plot(1:3);
subplot(2, 1, 2);
plot(1:3);
u2_chk('is_figure_nextplot_axes', @() numel(findobj(f, 'Type', 'axes')));
close(f);

f = figure('Visible', 'off');
png = [tempname '.png'];
imwrite(uint8(reshape(1:48, 4, 4, 3) * 5), png);
h = image(imread(png));
u2_chk('is_image_of_imread', @() {char(get(gca, 'Visible')), class(h.CData), size(h.CData), h.CDataMapping});
close(f);
f = figure('Visible', 'off');
h = imshow(png);
u2_chk('is_file', @() {class(h.CData), size(h.CData), h.CDataMapping, get(gca, 'XLim')});
close(f);
delete(png);

u2_err('is_err_none', @() imshow());
u2_err('is_err_syntax', @() imshow(magic(4), 3));
u2_err('is_err_xy', @() imshow(1:4, 1:4, magic(4)));
u2_err('is_err_range', @() imshow(magic(4), [5 1]));
u2_err('is_err_param', @() imshow(magic(4), 'Bogus', 1));
u2_err('is_err_many', @() imshow(magic(4), [0 1], 3));
u2_err('is_err_inputs', @() imshow(magic(4), ones(2, 2)));
u2_err('is_err_odd', @() imshow(magic(4), 'XData'));
u2_err('is_err_ambiguous', @() imshow(magic(4), 'In', 1));
u2_err('is_err_later_param', @() imshow(magic(4), 'XData', [1 2], 'Bogus', 1));
u2_err('is_err_interpolation', @() imshow(magic(4), 'Interpolation', 'cubic'));
u2_err('is_err_border', @() imshow(magic(4), 'Border', 'wide'));
u2_err('is_err_colormap', @() imshow(magic(4), 'Colormap', [1 2]));
u2_err('is_err_colormap_name', @() imshow(magic(4), 'Colormap', 'jet'));
u2_err('is_err_coords', @() imshow(magic(4), 'XData', 5));
u2_err('is_err_magnification', @() imshow(magic(4), 'InitialMagnification', 'big'));
u2_err('is_err_type', @() imshow({1}));
u2_err('is_err_indexed', @() imshow(int16(magic(4)), eight()));
u2_err('is_err_rgb_class', @() imshow(int16(ones(4, 4, 3))));
u2_err('is_err_logical_3d', @() imshow(true(2, 2, 3)));
u2_err('is_err_planes', @() imshow(rand(4, 4, 2)));
u2_err('is_err_file', @() imshow('nofile.png'));
u2_err('is_err_nan', @() imshow(magic(4), [NaN 1]));
u2_err('is_err_parent', @() imshow(magic(4), 'Parent', 5));
u2_err('is_err_reduce', @() imshow(magic(4), 'Reduce', true));
u2_err('is_err_range_one', @() imshow(magic(4), 'DisplayRange', 5));
u2_err('is_err_map_values', @() imshow(magic(4), [0 1 2]));
close all force;

function s = types(f, tags)
s = strjoin(cellfun(@(t) strjoin(cellstr(get(findall(f, 'Tag', t), 'Type')), '+'), tags, 'UniformOutput', false), ' ');
end

function n = counts(f, tags, find)
n = cellfun(@(t) numel(find(f, 'Tag', t)), tags);
end

function [f, lg] = legend_figure()
f = figure('Visible', 'off');
plot(1:3);
hold on;
plot(3:-1:1);
lg = legend('up', 'down');
drawnow;
t = lg.Title;
t.String; % the first read of a fresh title is stale in R2025b
drawnow;
end

function s = assign_title(lg)
lg.Title = 'assigned';
s = lg.Title.String;
end

function s = attempt(fn)
% One line in both engines: the answer, or the refusal's identifier.
try
    s = fn();
catch err
    s = ['refused ' err.identifier];
end
end

function m = eight()
% A map of eight colours whose components are 0 or 1, so eight bits hold them exactly.
m = [0 0 1; 0 1 1; 0 1 0; 1 1 0; 1 0 0; 1 0 1; 0 0 0; 1 1 1];
end

function show(name, make)
f = figure('Visible', 'off');
make();
ax = gca;
im = findobj(ax, 'Type', 'image');
u2_chk([name '_axes'], @() {round(ax.XLim, 6), ax.XLimMode, round(ax.YLim, 6), ax.YLimMode, ax.YDir, ax.TickDir, char(ax.XGrid), char(ax.YGrid), ax.DataAspectRatio, ax.DataAspectRatioMode, ax.PlotBoxAspectRatioMode, char(ax.Visible), ax.Layer});
u2_chk([name '_colour'], @() colour(ax, im));
if ~isempty(im)
    u2_chk([name '_image'], @() {numel(im), class(im(1).CData), size(im(1).CData), im(1).CDataMapping, im(1).XData, im(1).YData, im(1).Interpolation, im(1).BusyAction, char(im(1).Interruptible)});
end
u2_chk([name '_figure'], @() {get(f, 'NextPlot'), numel(findobj(f, 'Type', 'axes')), numel(ax.Children)});
close(f);
end

function c = colour(ax, im)
% The colormap is the picture's own for a grey or indexed one; a true-colour picture leaves the axes'
% default, whose parula rows this build holds by its own stops. Colours are eight bits here.
c = {round(ax.CLim, 6), ax.CLimMode};
if isempty(im) || ndims(im(1).CData) < 3
    c = [c, {size(ax.Colormap), round(ax.Colormap(1, :), 2), round(ax.Colormap(end, :), 2)}];
end
end

function place(name, make)
f = figure('Visible', 'off');
make();
u2_chk(name, @() round(get(gca, 'Position'), 4));
close(f);
end

function then(a, b)
a();
b();
end

function held_imshow()
hold on;
imshow(magic(4) / 16);
end
