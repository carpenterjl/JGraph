function s = u5_cell(h)
% Where a grid's child sits: its Layout.Row and Layout.Column on one line (U5 fixtures).
o = get(h, 'Layout');
s = ['r' mat2str(double(o.Row)) ' c' mat2str(double(o.Column))];
end
