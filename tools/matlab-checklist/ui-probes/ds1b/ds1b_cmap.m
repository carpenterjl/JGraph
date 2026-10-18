% DS1b (designer plan): whether an axes' Colormap setter takes a colormap's name, and how exactly App
% Designer's getColormapString must find a map's rows to name it. Headless.
fig = uifigure('Visible', 'off'); ax = uiaxes(fig);
try
    ax.Colormap = 'hot'; fprintf('name accepted: %d\n', isequal(ax.Colormap, hot(256)));
catch e
    fprintf('name refused: %s\n', e.message);
end
fprintf('colormap(ax,''hot'') rows %d; hot() rows %d\n', size(colormap(ax, 'hot'), 1), size(hot, 1));
fprintf('getColormapString: %s\n', inspector.internal.getColormapString(hot(256)));
fprintf('getColormapString 64: %s\n', inspector.internal.getColormapString(hot(64)));
delete(fig);
