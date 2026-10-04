% U0 item 1: what a MATLAB pixel is on a display scaled above 100 % (windows open ~20 s).
r = groot;
fprintf('ScreenSize=%s ScreenPixelsPerInch=%g MonitorPositions=%s\n', mat2str(r.ScreenSize), r.ScreenPixelsPerInch, mat2str(r.MonitorPositions));
f = figure('Name','JG-DPI-figure','NumberTitle','off','Units','pixels','Position',[100 100 400 300],'MenuBar','none','ToolBar','none','Color',[1 0 0]);
u = uifigure('Name','JG-DPI-uifigure','Position',[600 100 400 300],'Color',[0 0 1]);
drawnow; pause(4); drawnow;
fprintf('figure   Position=%s OuterPosition=%s InnerPosition=%s\n', mat2str(f.Position), mat2str(f.OuterPosition), mat2str(f.InnerPosition));
fprintf('uifigure Position=%s OuterPosition=%s InnerPosition=%s\n', mat2str(u.Position), mat2str(u.OuterPosition), mat2str(u.InnerPosition));
fr = getframe(f); fprintf('getframe(figure) size=%s\n', mat2str(size(fr.cdata,[1 2])));
try, fr2 = getframe(u); fprintf('getframe(uifigure) size=%s\n', mat2str(size(fr2.cdata,[1 2]))); catch e, fprintf('getframe(uifigure) %s\n', e.message); end
fn = fullfile(pwd,'u0_dpi_app.png'); exportapp(u, fn); im = imread(fn); fprintf('exportapp(uifigure) size=%s\n', mat2str(size(im,[1 2])));
fn = fullfile(pwd,'u0_dpi_fig.png'); exportapp(f, fn); im = imread(fn); fprintf('exportapp(figure) size=%s\n', mat2str(size(im,[1 2])));
print(f, '-dpng', '-r0', fullfile(pwd,'u0_dpi_print.png')); im = imread(fullfile(pwd,'u0_dpi_print.png')); fprintf('print -r0 size=%s\n', mat2str(size(im,[1 2])));
c = uicontrol(f,'Style','pushbutton','String','X','Units','pixels','Position',[20 20 100 40]);
try, print(f, '-dpng', fullfile(pwd,'u0_dpi_print2.png')); fprintf('print with uicontrol (display): ok\n'); catch e, fprintf('print with uicontrol (display): %s | %s\n', e.identifier, e.message); end
fprintf('READY\n');
pause(14);
delete(f); delete(u);
