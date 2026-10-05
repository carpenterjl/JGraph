function s = u5_tracks(g)
% A grid's tracks on one line: R{...} C{...} (U5 fixtures).
s = ['R{' u5_tracklist(get(g, 'RowHeight')) '} C{' u5_tracklist(get(g, 'ColumnWidth')) '}'];
end
