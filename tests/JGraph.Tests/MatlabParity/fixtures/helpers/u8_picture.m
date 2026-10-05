function s = u8_picture(h)
% A tool's two pictures on one line: its CData and its Icon, by class and size (U8 fixtures).
c = get(h, 'CData');
i = get(h, 'Icon');
if ischar(i)
    icon = ['''' i ''''];
else
    icon = [class(i) ' ' mat2str(size(i))];
end
s = sprintf('cdata %s %s icon %s', class(c), mat2str(size(c)), icon);
end
