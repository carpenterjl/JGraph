function u4_box(name, h)
% A message box's figure and object tree as CHK lines (U4 fixtures). What depends only on
% R2025b's layout constants is exact; what depends on how wide a line of text measures is held to
% a few points, because each engine measures the font its own window draws.
if ~(isscalar(h) && ishghandle(h))
    fprintf('CHK|%s_made|0|exact\n', name);
    return
end
fprintf('CHK|%s_figure|[%s] %s %s %s [%s] %s %s %s %s %s|exact\n', name, get(h, 'Name'), get(h, 'WindowStyle'), ...
    char(get(h, 'Resize')), get(h, 'Units'), get(h, 'Tag'), get(h, 'HandleVisibility'), char(get(h, 'IntegerHandle')), ...
    char(get(h, 'NumberTitle')), get(h, 'MenuBar'), get(h, 'ToolBar'));
% The allowance grows with the lines of text, since each line's height is the engine's own measure.
t = findall(h, 'Type', 'text');
n = 1;
if ~isempty(t)
    s = get(t, 'String');
    if ischar(s), n = max(1, size(s, 1)); else, n = max(1, numel(s)); end
end
p = get(h, 'Position');
fprintf('CHK|%s_width|%.10g|abs=8\n', name, p(3));
fprintf('CHK|%s_height|%.10g|abs=%d\n', name, p(4), 5 + 3 * n);
kids = allchild(h);
types = {};
for k = 1:numel(kids)
    if ~strcmp(get(kids(k), 'Type'), 'annotationpane')
        types{end + 1} = get(kids(k), 'Type'); %#ok<AGROW>
    end
end
fprintf('CHK|%s_children|%s|exact\n', name, strjoin(types, ' '));
ok = findall(h, 'Tag', 'OKButton');
if ~isempty(ok)
    q = get(ok, 'Position');
    fprintf('CHK|%s_ok|%s %s [%s] %g %s %s %.10g %.10g %.10g|exact\n', name, get(ok, 'Style'), get(ok, 'Units'), get(ok, 'String'), ...
        get(ok, 'FontSize'), get(ok, 'FontName'), get(ok, 'HorizontalAlignment'), q(2), q(3), q(4));
    fprintf('CHK|%s_ok_x|%.10g|abs=4\n', name, q(1));
    fprintf('CHK|%s_ok_callback|%s %s|exact\n', name, get(ok, 'Callback'), class(get(ok, 'KeyPressFcn')));
end
if ~isempty(t)
    lines = s;
    if ischar(s), lines = cellstr(s); end
    q = get(t, 'Position');
    e = get(t, 'Extent');
    fprintf('CHK|%s_text|[%s] %s %g %s %s %s %s|exact\n', name, get(t, 'Tag'), get(t, 'Units'), get(t, 'FontSize'), ...
        get(t, 'FontName'), get(t, 'HorizontalAlignment'), get(t, 'VerticalAlignment'), get(t, 'Interpreter'));
    fprintf('CHK|%s_text_lines|%s %s %s|exact\n', name, class(s), mat2str(size(s)), strrep(strjoin(lines(:)', '/'), '|', '!'));
    fprintf('CHK|%s_text_x|%.10g|exact\n', name, q(1));
    fprintf('CHK|%s_text_y|%.10g|abs=%d\n', name, q(2), 2 + 2 * n);
    fprintf('CHK|%s_text_extent_w|%.10g|abs=8\n', name, e(3));
    fprintf('CHK|%s_text_extent_h|%.10g|abs=%d\n', name, e(4), 2 + n);
end
ia = findall(h, 'Tag', 'IconAxes');
fprintf('CHK|%s_has_icon|%d|exact\n', name, ~isempty(ia));
if ~isempty(ia)
    q = get(ia, 'Position');
    im = findall(ia, 'Type', 'image');
    c = get(im, 'CData');
    fprintf('CHK|%s_icon|%s %.10g %.10g %.10g %s %s %s %s|exact\n', name, get(ia, 'Units'), q(1), q(3), q(4), ...
        mat2str(get(ia, 'XLim')), mat2str(get(ia, 'YLim')), get(ia, 'YDir'), char(get(ia, 'Visible')));
    fprintf('CHK|%s_icon_y|%.10g|abs=%d\n', name, q(2), 2 + 2 * n);
    fprintf('CHK|%s_icon_image|%s|exact\n', name, mat2str(size(c)));
end
end
