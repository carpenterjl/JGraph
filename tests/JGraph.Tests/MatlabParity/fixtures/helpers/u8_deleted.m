function s = u8_deleted(p)
% A tab group deleted: whether its tab and what the tab held are still there (U8 fixtures).
tg = uitabgroup(p);
t = uitab(tg);
b = uipanel(t);
delete(tg);
s = sprintf('%d %d', isgraphics(t), isgraphics(b));
end
