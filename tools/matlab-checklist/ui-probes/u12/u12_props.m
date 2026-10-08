% u12_props - for each name R2021b documents and this build's get(h) list leaves out (probe-properties,
% U12), whether R2025b still answers it: in get(h)'s list, through isprop, and through get(h, name).
rows = {
    'groot',                                 {'CallbackObject','FixedWidthFontName','PointerLocation','ScreenDepth'}
    'uicontrol(figure)',                  {'HitTest','Selected','SelectionHighlight','TooltipString'}
    'uipanel(uifigure)',                     {'HitTest'}
    'uibuttongroup(uifigure)',               {'HitTest'}
    'uitoolbar(figure)',                     {'HitTest'}
    'uipushtool(uitoolbar(figure))',         {'HitTest','TooltipString'}
    'uitoggletool(uitoolbar(figure))',       {'HitTest','TooltipString'}
    'uitable(uifigure)',                     {'Extent','HitTest','RearrangeableColumns'}
    'axtoolbarbtn(axtoolbar(axes(figure)), ''push'')',  {'ButtonPushedFcn'}
    'axtoolbarbtn(axtoolbar(axes(figure)), ''state'')', {'ValueChangedFcn'}
    'uiprogressdlg(uifigure)',               {'Type'}
    'uistyle(''FontWeight'',''bold'')',      {'Type'}
};
for k = 1:size(rows, 1)
    h = eval(rows{k, 1});
    try
        listed = fieldnames(get(h));
    catch ME
        listed = {};
        fprintf('%s: get(h) refused: %s\n', rows{k, 1}, ME.message);
    end
    for name = rows{k, 2}
        n = name{1};
        try
            v = get(h, n); %#ok<NASGU>
            got = 'get ok';
        catch ME
            got = ['get refused: ' ME.message];
        end
        fprintf('%s | %s | listed=%d isprop=%d | %s\n', rows{k, 1}, n, any(strcmp(listed, n)), isprop(h, n), got);
    end
end
% the probe's own steps on the two that failed it
s = uistyle('FontWeight', 'bold');
t = s(1); fprintf('uistyle s(1): %s\n', class(t));
d = uiprogressdlg(uifigure);
fprintf('progressdlg fields: %s\n', strjoin(fieldnames(get(d))', ' '));
fprintf('style fields: %s\n', strjoin(fieldnames(get(s))', ' '));
fprintf('contour one output: %s %s\n', class(contour(peaks(8))), mat2str(size(contour(peaks(8)))));
