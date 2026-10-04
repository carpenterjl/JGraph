function u4_messages
% U4 probe (headless): the words of R2025b's dialog messages, read from its message catalogue.
ids = {
    'MATLAB:questdlg:StringMismatch'
    'MATLAB:questdlg:TooFewArguments'
    'MATLAB:questdlg:WrongNumberOutputs'
    'MATLAB:questdlg:TooManyInputs'
    'MATLAB:inputdlg:IncorrectSize'
    'MATLAB:inputdlg:InvalidDefaultAnswer'
    'MATLAB:inputdlg:InvalidInput'
    'MATLAB:listdlg:InvalidArgument'
    'MATLAB:listdlg:NeedParameter'
    'MATLAB:msgbox:colormap'
    'MATLAB:msgbox:IncorrectIconColormap'
    'MATLAB:waitbar:InvalidOptionalArgsPass'
    'MATLAB:waitbar:WaitbarHandlesNotFound'
    'MATLAB:waitbar:ImproperArguments'
    'MATLAB:uistring:popupdialogs:InputDlgInput'
    'MATLAB:uistring:popupdialogs:SelectAll'
    'MATLAB:uistring:popupdialogs:OK'
    'MATLAB:uistring:popupdialogs:Cancel'
    'MATLAB:uistring:popupdialogs:Yes'
    'MATLAB:uistring:popupdialogs:No'
    'MATLAB:uistring:waitbar:WaitbarLabel'
    'MATLAB:uistring:waitbar:Cancel'
    'MATLAB:uistring:uiopen:DialogOpen'
    'MATLAB:uistring:filedialogs:SelectFileToOpen'
    'MATLAB:uistring:filedialogs:SelectFileToWrite'
    'MATLAB:uistring:filedialogs:SelectFolderToOpen'
    'MATLAB:uistring:uisetcolor:Color'
    'MATLAB:uistring:uisetfont:Font'
    'MATLAB:uigetdir:TooManyInputs'
    'MATLAB:uisetcolor:InvalidParameterList'
    'MATLAB:uisetfont:InvalidParameterList'
    'MATLAB:hg:NonInteractiveFunctionSupport'
    };
for k = 1:numel(ids)
    try
        fprintf('%s => [%s]\n', ids{k}, getString(message(ids{k})));
    catch e
        fprintf('%s => (no such message: %s)\n', ids{k}, e.identifier);
    end
end
try, getString(message('MATLAB:listdlg:UnknownParameter', 'Bogus')), catch e, disp(e.message), end
fprintf('UnknownParameter => [%s]\n', getString(message('MATLAB:listdlg:UnknownParameter', 'Bogus')));
fprintf('DefaultFigurePosition=%s DefaultUicontrolFontSize=%g DefaultTextFontSize=%g DefaultTextFontName=%s DefaultAxesFontSize=%g\n', ...
    mat2str(get(0, 'DefaultFigurePosition')), get(0, 'DefaultUicontrolFontSize'), get(0, 'DefaultTextFontSize'), get(0, 'DefaultTextFontName'), get(0, 'DefaultAxesFontSize'));
% the argument checks of the native dialogs that come before the display check
tries = {
    'uigetdir 3 args', @() uigetdir('a', 'b', 'c')
    'uigetdir number', @() uigetdir(5)
    'uisetcolor 3 args', @() uisetcolor([1 0 0], 'a', 'b')
    'uisetcolor bad', @() uisetcolor('zzz', 'a')
    'uisetfont 3 args', @() uisetfont(struct(), 'a', 'b')
    'uigetfile number', @() uigetfile(5)
    'uigetfile odd', @() uigetfile('*.m', 'T', 'x.m', 'MultiSelect')
    'uigetfile bad option', @() uigetfile('*.m', 'T', 'x.m', 'Bogus', 'on')
    'uiputfile number', @() uiputfile(5)
    'uisave number', @() uisave(5)
    'uiopen number', @() uiopen(5)
    'uiload arg', @() uiload(5)
    'exportapp 3 args', @() exportapp(figure('Visible', 'off'), 'a.png', 3)
    'exportapp not a figure', @() exportapp(5.5, 'a.png')
    'exportapp bad extension', @() exportapp(figure('Visible', 'off'), 'a.xyz')
    'exportapp no file', @() exportapp(figure('Visible', 'off'))
    };
for k = 1:size(tries, 1)
    try
        tries{k, 2}();
        fprintf('%s: ok\n', tries{k, 1});
    catch e
        fprintf('%s: ERR [%s] %s\n', tries{k, 1}, e.identifier, strrep(e.message, newline, ' / '));
    end
end
delete(findall(0, 'Type', 'figure'));
end
