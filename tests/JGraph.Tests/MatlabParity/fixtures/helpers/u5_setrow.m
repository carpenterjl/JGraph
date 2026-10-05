function s = u5_setrow(h, value)
% Writes Layout.Row and answers where the child then sits (U5 fixtures).
h.Layout.Row = value;
s = u5_cell(h);
end
