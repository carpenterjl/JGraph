function s = u8_font(t)
% A table's font size written in points and read in pixels and in points again (U8 fixtures).
set(t, 'FontUnits', 'points');
set(t, 'FontSize', 20);
a = get(t, 'FontSize');
set(t, 'FontUnits', 'pixels');
b = get(t, 'FontSize');
set(t, 'FontUnits', 'points');
c = get(t, 'FontSize');
s = sprintf('%.6g %.6g %.6g', a, b, c);
end
