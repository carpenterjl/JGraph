function s = u9_style(st)
% A uistyle's ten properties as one line (U9 fixtures).
names = {'BackgroundColor', 'FontColor', 'FontWeight', 'FontAngle', 'FontName', 'HorizontalAlignment', 'HorizontalClipping', 'IconAlignment', 'Interpreter', 'Icon'};
parts = cell(1, numel(names));
for k = 1:numel(names)
    parts{k} = [names{k} '=' u9_text(st.(names{k}))];
end
s = strjoin(parts, '; ');
end
