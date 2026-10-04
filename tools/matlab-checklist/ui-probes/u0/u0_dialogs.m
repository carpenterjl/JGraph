% U0: what the classic dialogs return under -batch -noFigureWindows, and their child layouts.
say('batch=%d', batchStartupOptionUsed);
f0 = figure; say('plain figure Visible=%s', char(f0.Visible)); delete(f0);

tries = {
  'msgbox',    @() msgbox('Hello there','Title')
  'msgbox_modal', @() msgbox('Hello there','Title','modal')
  'msgbox_icon', @() msgbox('Hello there','Title','warn')
  'errordlg',  @() errordlg('Bad value','Error')
  'errordlg_modal', @() errordlg('Bad value','Error','modal')
  'warndlg',   @() warndlg('Careful','Warn')
  'helpdlg',   @() helpdlg('Help text','Help')
  'waitbar',   @() waitbar(0.25,'Working...')
  'dialog',    @() dialog('Name','Dlg')
  'questdlg',  @() questdlg('Proceed?','Q','Yes','No','Yes')
  'inputdlg',  @() inputdlg({'Name:','Age:'},'Input',[1 35],{'a','1'})
  'listdlg',   @() listdlg('ListString',{'a','b','c'})
};
for k = 1:size(tries,1)
    name = tries{k,1};
    t = tic;
    try
        r = tries{k,2}();
        say('== %s: returned class=%s size=%s in %.2fs', name, class(r), mat2str(size(r)), toc(t));
        if isa(r,'matlab.ui.Figure') && isvalid(r)
            dumpFigure(r);
            delete(r);
        elseif ~isempty(r) && (ischar(r) || iscell(r) || isnumeric(r))
            disp(r);
        end
    catch e
        say('== %s: ERROR %s | %s (%.2fs)', name, e.identifier, e.message, toc(t));
    end
    close all force
end
% uiwait under -batch -noFigureWindows, with a timeout and with a timer that resumes.
f = figure; t = tic; uiwait(f, 2); say('uiwait(f,2) returned after %.2fs, WaitStatus=%s', toc(t), f.WaitStatus);
tm = timer('StartDelay',1.5,'TimerFcn',@(~,~) uiresume(f)); start(tm);
t = tic; uiwait(f); say('uiwait(f) + timer uiresume returned after %.2fs', toc(t)); delete(tm);
tm = timer('StartDelay',1.5,'TimerFcn',@(~,~) delete(f)); start(tm);
t = tic; uiwait(f); say('uiwait(f) + timer delete returned after %.2fs, valid=%d', toc(t), isvalid(f)); delete(tm);
% uiwait with nothing that can end it: does -batch hang? (The runner times out at 240 s.)
g = figure; t = tic;
tm = timer('StartDelay',20,'TimerFcn',@(~,~) fprintf('watchdog fired at %.1fs, deleting\n', toc(t))); start(tm);
tm2 = timer('StartDelay',25,'TimerFcn',@(~,~) delete(g)); start(tm2);
uiwait(g); say('dead uiwait returned after %.2fs', toc(t));
delete([tm tm2]);

function dumpFigure(h)
    say('   figure: Name=%s WindowStyle=%s Resize=%s Units=%s Position=%s Color=%s Visible=%s Tag=%s HandleVisibility=%s IntegerHandle=%s NumberTitle=%s MenuBar=%s ToolBar=%s', ...
        h.Name, h.WindowStyle, char(h.Resize), h.Units, mat2str(h.Position,6), mat2str(h.Color,4), ...
        char(h.Visible), h.Tag, h.HandleVisibility, char(h.IntegerHandle), char(h.NumberTitle), h.MenuBar, h.ToolBar);
    ad = getappdata(h); fn = fieldnames(ad);
    if ~isempty(fn), say('   appdata: %s', strjoin(fn', ', ')); end
    walk(h, '   ');
end
function walk(h, ind)
    kids = allchild(h);
    for i = numel(kids):-1:1
        c = kids(i);
        s = sprintf('%s- %s', ind, c.Type);
        if isprop(c,'Style'), s = [s ' Style=' c.Style]; end
        if isprop(c,'Tag') && ~isempty(c.Tag), s = [s ' Tag=' c.Tag]; end
        if isprop(c,'Units'), s = [s ' Units=' c.Units]; end
        if isprop(c,'Position'), s = [s ' Position=' mat2str(c.Position,6)]; end
        if isprop(c,'String'), v = c.String; if iscell(v), v = strjoin(v,'|'); end; if ischar(v) && size(v,1)==1, s = [s ' String="' v '"']; end; end
        if isprop(c,'FontSize'), s = sprintf('%s FontSize=%g', s, c.FontSize); end
        if isprop(c,'FontName'), s = [s ' FontName=' c.FontName]; end
        if isprop(c,'HorizontalAlignment'), s = [s ' HAlign=' c.HorizontalAlignment]; end
        if strcmp(c.Type,'patch'), s = [s ' FaceColor=' mat2str(c.FaceColor,4) ' XData=' mat2str(c.XData',4)]; end
        if strcmp(c.Type,'image'), s = [s ' CData=' mat2str(size(c.CData))]; end
        if strcmp(c.Type,'axes'), s = [s ' XLim=' mat2str(c.XLim) ' Visible=' char(c.Visible)]; end
        if strcmp(c.Type,'text'), s = [s ' TextString="' char(string(c.String)) '"']; end
        say('%s', s);
        if ~strcmp(c.Type,'uimenu'), walk(c, [ind '  ']); end
    end
end
function say(fmt, varargin)
    fprintf([fmt '\n'], varargin{:});
end
