function u4_nargout
% U4: nargout and exist for the names this stage adds, in the form nargout-r2025b.tsv keeps.
names = {'uiwait', 'uiresume', 'guihandles', 'dialog', 'msgbox', 'errordlg', 'warndlg', 'helpdlg', 'waitbar', ...
    'questdlg', 'inputdlg', 'listdlg', 'uigetfile', 'uiputfile', 'uigetdir', 'uisetcolor', 'uisetfont', 'uiopen', ...
    'uisave', 'uiload', 'exportapp', 'waitfor', 'getappdata', 'setappdata', 'isappdata', 'rmappdata', 'guidata'};
for k = 1:numel(names)
    try
        n = nargout(names{k});
    catch
        n = NaN;
    end
    fprintf('%s\t%d\t%d\n', names{k}, n, exist(names{k})); %#ok<EXIST>
end
end
