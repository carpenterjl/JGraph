function k0_figure(varargin)
%K0_FIGURE Graphics in a console standalone with no window shown: an invisible figure is drawn,
%exported and closed, and the app must still end on its own.
k0_quiet;
out = fullfile(tempdir, 'k0_figure.png');
if exist(out, 'file'), delete(out); end
f = figure('Visible', 'off');
plot(1:3, [2 4 3]);
title('k0');
k0_do('exportgraphics', @() exportgraphics(f, out));
k0_try('exported.bytes.positive', @() dirBytes(out) > 0);
k0_do('print', @() print(f, '-dpng', fullfile(tempdir, 'k0_print.png')));
k0_try('figure.Visible', @() char(f.Visible));
k0_try('groot.Children.count', @() numel(get(groot, 'Children')));
k0_try('waitforallfiguresclosed', @() waitforallfiguresclosed());
close(f);
k0_try('groot.Children.after', @() numel(get(groot, 'Children')));
fprintf('end\n');
end

function n = dirBytes(p)
d = dir(p);
n = d.bytes;
end
