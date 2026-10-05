function s = u5_tracklist(c)
% One direction's tracks as text, numbers as written by num2str (U5 fixtures).
parts = cell(1, numel(c));
for k = 1:numel(c)
    if ischar(c{k})
        parts{k} = c{k};
    else
        parts{k} = num2str(double(c{k}));
    end
end
s = strjoin(parts, ',');
end
