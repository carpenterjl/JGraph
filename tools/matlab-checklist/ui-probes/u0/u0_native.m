% U0: native dialogs under -batch -noFigureWindows (expected to refuse, not to open a window).
calls = {'uigetfile', @() uigetfile('*.m'); 'uiputfile', @() uiputfile('*.m'); 'uigetdir', @() uigetdir(); ...
         'uisetcolor', @() uisetcolor([1 0 0]); 'uisetfont', @() uisetfont(); 'uiopen', @() uiopen('load'); ...
         'uisave', @() uisave('x'); 'uiload', @() uiload(); 'inputdlg', @() inputdlg('a')};
for k = 1:size(calls,1)
    t = tic;
    try
        r = calls{k,2}(); fprintf('%-10s returned %s %s after %.1fs\n', calls{k,1}, class(r), mat2str(size(r)), toc(t));
    catch e
        fprintf('%-10s ERROR %s | %s\n', calls{k,1}, e.identifier, e.message);
    end
end
