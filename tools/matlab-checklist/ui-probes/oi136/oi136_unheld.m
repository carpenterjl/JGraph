% oi136_unheld - open item 137, third probe: each maker given a group first, and given 'Parent' as a
% group, when the axes is not held - whether the group survives, where the object goes and what else
% the axes keeps. Headless in R2025b and in JGraph.
set(groot, 'DefaultFigureVisible', 'off');
f = figure;
ax = axes(f);
x = 1:3; y = [2 1 3];
C = magic(3);
makers = {
    'plot',      @(p) plot(p, x, y)
    'plot3',     @(p) plot3(p, x, y, y)
    'semilogy',  @(p) semilogy(p, x, y)
    'stairs',    @(p) stairs(p, x, y)
    'stem',      @(p) stem(p, x, y)
    'stem3',     @(p) stem3(p, x, y, y)
    'scatter',   @(p) scatter(p, x, y)
    'bar',       @(p) bar(p, y)
    'barh',      @(p) barh(p, y)
    'area',      @(p) area(p, x, y)
    'errorbar',  @(p) errorbar(p, x, y, y)
    'histogram', @(p) histogram(p, [1 2 2])
    'fill',      @(p) fill(p, x, y, 'r')
    'fill3',     @(p) fill3(p, x, y, y, 'r')
    'imagesc',   @(p) imagesc(p, C)
    'image',     @(p) image(p, C)
    'line',      @(p) line(p, x, y)
    'patch',     @(p) patch(p, x, y, 'r')
    'surface',   @(p) surface(p, C)
    'text',      @(p) text(p, 1, 1, 'a')
    'rectangle', @(p) rectangle(p)
    'light',     @(p) light(p)
    };
for k = 1:size(makers, 1)
    for kind = {'hggroup', 'hgtransform'}
        cla(ax, 'reset');
        hold(ax, 'off');
        plot(ax, x, y, 'Tag', 'before');
        if strcmp(kind{1}, 'hggroup'), g = hggroup(ax); else, g = hgtransform(ax); end
        try
            h = makers{k, 2}(g);
            fprintf('%-10s %-11s first:  ok parent=%s group valid=%d axes children=%d held=%s\n', makers{k, 1}, kind{1}, ...
                h(1).Parent.Type, isvalid(g), numel(ax.Children), ax.NextPlot);
        catch e
            fprintf('%-10s %-11s first:  err %s | %s | group valid=%d\n', makers{k, 1}, kind{1}, e.identifier, e.message, isvalid(g));
        end
    end
end
disp('done');
