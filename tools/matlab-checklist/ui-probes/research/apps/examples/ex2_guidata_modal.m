function result = ex2_guidata_modal(defaultName, defaultGain)
% EX2_GUIDATA_MODAL  A blocking input dialog in the GUIDE-era style, written by hand:
% handles struct stored with guidata, callbacks are local functions taking
% (hObject, eventdata), the caller blocks in uiwait until OK/Cancel/close
% calls uiresume, then reads the answer back out of guidata and deletes the figure.
%
%   r = ex2_guidata_modal('run1', 2.5)   % r is a struct, or [] if cancelled

if nargin < 1, defaultName = 'run1'; end
if nargin < 2, defaultGain = 1; end

hFig = figure('Name', 'Run settings', 'NumberTitle', 'off', 'MenuBar', 'none', ...
    'Resize', 'off', 'WindowStyle', 'modal', 'Units', 'pixels', ...
    'Position', [400 400 320 170], 'Tag', 'settingsFig', ...
    'CloseRequestFcn', @onCancel);

handles = guihandles(hFig);          % empty struct apart from the figure's own Tag
handles.figure = hFig;
handles.nameEdit = uicontrol(hFig, 'Style', 'edit', 'Tag', 'nameEdit', ...
    'String', defaultName, 'Position', [110 120 190 24]);
uicontrol(hFig, 'Style', 'text', 'String', 'Name', 'Position', [20 120 80 20], ...
    'HorizontalAlignment', 'left');
handles.gainEdit = uicontrol(hFig, 'Style', 'edit', 'Tag', 'gainEdit', ...
    'String', num2str(defaultGain), 'Position', [110 85 190 24], ...
    'Callback', @onGainEdited);
uicontrol(hFig, 'Style', 'text', 'String', 'Gain', 'Position', [20 85 80 20], ...
    'HorizontalAlignment', 'left');
handles.logCheck = uicontrol(hFig, 'Style', 'checkbox', 'Tag', 'logCheck', ...
    'String', 'Log to file', 'Value', 0, 'Position', [110 55 190 22]);
uicontrol(hFig, 'Style', 'pushbutton', 'String', 'OK', 'Position', [110 15 90 28], ...
    'Callback', @onOK);
uicontrol(hFig, 'Style', 'pushbutton', 'String', 'Cancel', 'Position', [210 15 90 28], ...
    'Callback', @onCancel);

handles.result = [];
guidata(hFig, handles);

uiwait(hFig);                         % blocks; callbacks keep running

if ishghandle(hFig)
    handles = guidata(hFig);
    result = handles.result;
    delete(hFig);
else
    result = [];
end
end

function onGainEdited(hObject, ~)
handles = guidata(hObject);
v = str2double(get(hObject, 'String'));
if isnan(v)
    set(hObject, 'String', '1', 'ForegroundColor', [0.8 0 0]);
else
    set(hObject, 'ForegroundColor', [0 0 0]);
end
guidata(hObject, handles);
end

function onOK(hObject, ~)
handles = guidata(hObject);
handles.result = struct( ...
    'name', get(handles.nameEdit, 'String'), ...
    'gain', str2double(get(handles.gainEdit, 'String')), ...
    'log', logical(get(handles.logCheck, 'Value')));
guidata(hObject, handles);
uiresume(handles.figure);
end

function onCancel(hObject, ~)
handles = guidata(hObject);
handles.result = [];
guidata(hObject, handles);
uiresume(handles.figure);
end
