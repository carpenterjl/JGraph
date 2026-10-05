function s = u8_hidden(p)
% A tab group that is not shown, and what a panel in one of its tabs then says of itself (U8 fixtures).
tg = uitabgroup(p, 'Visible', 'off');
t = uitab(tg);
b = uipanel(t);
s = [char(get(tg, 'Visible')) ' ' char(get(b, 'Visible'))];
end
