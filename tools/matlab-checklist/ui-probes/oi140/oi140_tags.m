% oi140_tags - open item 140: what a savefig/openfig round trip keeps of each object's Tag, Type
% and UserData in R2025b, and which objects findobj reaches against findall. Headless. JGraph's
% own document lost the Tag of a text, a rectangle and an annotation, and turned a rectangle into a
% patch; this is the reference for each kind before and after the trip.
set(groot, 'DefaultFigureVisible', 'off');
fprintf('%s\n', version);

f = figure('Tag', 'fig');
ax = axes(f, 'Tag', 'ax');
hold(ax, 'on');
set(plot(ax, 1:3), 'Tag', 'line', 'UserData', 11);
text(ax, 1, 2, 'note', 'Tag', 'text', 'UserData', 12);
rectangle(ax, 'Position', [0.5 0.5 1 1], 'Curvature', [0.3 0.6], 'Tag', 'rect', 'UserData', 13);
patch(ax, [0 1 1], [0 0 1], 'r', 'Tag', 'patch');
surface(ax, [0 1; 0 1], [0 0; 1 1], [0 0; 0 0], 'Tag', 'surf');
image(ax, 'CData', magic(3), 'Tag', 'img');
light(ax, 'Tag', 'light');
g = hggroup(ax, 'Tag', 'group');
line(g, [0 1], [1 0], 'Tag', 'ingroup');
scatter(ax, 1:3, 3:-1:1, 'Tag', 'scatter');
bar(ax, 1:3, 'Tag', 'bar');
legend(ax, 'Tag', 'legend');
colorbar(ax, 'Tag', 'cbar');
annotation(f, 'textbox', [0.1 0.1 0.2 0.1], 'String', 'tb', 'Tag', 'a_textbox', 'UserData', 21);
annotation(f, 'rectangle', [0.3 0.3 0.2 0.2], 'Tag', 'a_rectangle');
annotation(f, 'ellipse', [0.5 0.5 0.2 0.2], 'Tag', 'a_ellipse');
annotation(f, 'line', [0.1 0.4], [0.9 0.8], 'Tag', 'a_line');
annotation(f, 'arrow', [0.1 0.4], [0.7 0.6], 'Tag', 'a_arrow');
annotation(f, 'doublearrow', [0.1 0.4], [0.5 0.4], 'Tag', 'a_doublearrow');
annotation(f, 'textarrow', [0.1 0.4], [0.3 0.2], 'String', 'ta', 'Tag', 'a_textarrow');
uicontrol(f, 'Style', 'pushbutton', 'Tag', 'button');

tags = {'fig', 'ax', 'line', 'text', 'rect', 'patch', 'surf', 'img', 'light', 'group', 'ingroup', 'scatter', 'bar', ...
    'legend', 'cbar', 'a_textbox', 'a_rectangle', 'a_ellipse', 'a_line', 'a_arrow', 'a_doublearrow', 'a_textarrow', 'button'};
report('before', f, tags);
tmp = [tempname '.fig'];
savefig(f, tmp);
delete(f);
h = openfig(tmp, 'invisible');
report('after', h, tags);
r = findobj(h, 'Tag', 'rect');
fprintf('rect after: Position=%s Curvature=%s FaceColor=%s EdgeColor=%s LineWidth=%g\n', mat2str(r.Position), mat2str(r.Curvature), v2s(r.FaceColor), v2s(r.EdgeColor), r.LineWidth);
fprintf('UserData after: line=%s text=%s rect=%s textbox=%s\n', v2s(get(findobj(h, 'Tag', 'line'), 'UserData')), ...
    v2s(get(findobj(h, 'Tag', 'text'), 'UserData')), v2s(get(findobj(h, 'Tag', 'rect'), 'UserData')), v2s(get(findall(h, 'Tag', 'a_textbox'), 'UserData')));
delete(h);
delete(tmp);
fprintf('done\n');

function report(when, f, tags)
fprintf('--- %s\n', when);
for k = 1:numel(tags)
    t = tags{k};
    o = findobj(f, 'Tag', t);
    a = findall(f, 'Tag', t);
    types = '';
    if ~isempty(a)
        types = strjoin(cellstr(get(a, 'Type'))', ' ');
    end
    fprintf('  %-14s findobj=%d findall=%d type=%s\n', t, numel(o), numel(a), types);
end
end

function s = v2s(x)
if ischar(x)
    s = ['''' x ''''];
elseif isnumeric(x) || islogical(x)
    s = mat2str(x);
else
    s = ['<' class(x) '>'];
end
end
