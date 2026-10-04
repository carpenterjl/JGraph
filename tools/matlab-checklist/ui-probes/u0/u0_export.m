% U0: which export calls include UI components, in a figure and in a uifigure (headless).
% Components are painted pure red, panels pure green, the axes line pure blue, so presence is a pixel count.
outdir = fullfile(pwd,'u0_export_out'); if ~exist(outdir,'dir'), mkdir(outdir); end
% classic figure
f = figure('Units','pixels','Position',[100 100 400 300],'Color','w');
ax = axes(f,'Units','pixels','Position',[40 40 200 200]); plot(ax,[0 1],[0 1],'b','LineWidth',4);
uicontrol(f,'Style','pushbutton','Units','pixels','Position',[260 200 100 40],'BackgroundColor',[1 0 0],'String','');
uicontrol(f,'Style','edit','Units','pixels','Position',[260 140 100 30],'BackgroundColor',[1 0 0]);
uipanel(f,'Units','pixels','Position',[260 40 100 80],'BackgroundColor',[0 1 0]);
runAll(f,'fig',outdir);
% uifigure
uf = uifigure('Visible','off','Position',[100 100 400 300],'Color','w');
uax = uiaxes(uf,'Position',[10 40 230 220]); plot(uax,[0 1],[0 1],'b','LineWidth',4);
uibutton(uf,'Position',[260 200 100 40],'BackgroundColor',[1 0 0],'Text','');
uieditfield(uf,'Position',[260 140 100 30],'BackgroundColor',[1 0 0]);
uipanel(uf,'Position',[260 40 100 80],'BackgroundColor',[0 1 0]);
pause(2); drawnow;
runAll(uf,'uifig',outdir);
% uicontrol in a uifigure, uibutton in a figure
g = figure('Units','pixels','Position',[100 100 400 300],'Color','w');
uibutton(g,'Position',[260 200 100 40],'BackgroundColor',[1 0 0],'Text','');
runAll(g,'fig_uibutton',outdir);

function runAll(f, tag, outdir)
    calls = {
      'exportgraphics', @(fn) exportgraphics(f, fn)
      'exportgraphics_ax', @(fn) exportgraphics(findall(f,'Type','axes'), fn)
      'print', @(fn) print(f, '-dpng', '-r96', fn)
      'saveas', @(fn) saveas(f, fn)
      'exportapp', @(fn) exportapp(f, fn)
    };
    for k = 1:size(calls,1)
        fn = fullfile(outdir, sprintf('%s_%s.png', tag, calls{k,1}));
        try
            calls{k,2}(fn);
            report(tag, calls{k,1}, imread(fn));
        catch e
            fprintf('%-13s %-18s ERROR %s | %s\n', tag, calls{k,1}, e.identifier, e.message);
        end
    end
    try
        fr = getframe(f); report(tag, 'getframe', fr.cdata);
    catch e
        fprintf('%-13s %-18s ERROR %s | %s\n', tag, 'getframe', e.identifier, e.message);
    end
end
function report(tag, call, im)
    r = im(:,:,1); g = im(:,:,2); b = im(:,:,3);
    red = nnz(r > 200 & g < 60 & b < 60); green = nnz(g > 200 & r < 60 & b < 60); blue = nnz(b > 200 & r < 60 & g < 60);
    fprintf('%-13s %-18s size=%s red=%d green=%d blue=%d\n', tag, call, mat2str(size(im,[1 2])), red, green, blue);
end
