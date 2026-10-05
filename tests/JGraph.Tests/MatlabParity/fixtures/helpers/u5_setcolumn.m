function s = u5_setcolumn(h, value)
% Writes Layout.Column and answers where the child then sits (U5 fixtures).
h.Layout.Column = value;
s = u5_cell(h);
end
