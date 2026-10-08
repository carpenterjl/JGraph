function varargout = u11_guidex(varargin)
% U11_GUIDEX A GUIDE app exported to one file (U11 fixtures): gui_LayoutFcn builds the figure, so
% there is no .fig. Like GUIDE's exporter it keeps its singleton itself.
gui_Singleton = 1;
gui_State = struct('gui_Name',       mfilename, ...
                   'gui_Singleton',  gui_Singleton, ...
                   'gui_OpeningFcn', @u11_guidex_OpeningFcn, ...
                   'gui_OutputFcn',  @u11_guidex_OutputFcn, ...
                   'gui_LayoutFcn',  @u11_guidex_LayoutFcn, ...
                   'gui_Callback',   []);
if nargin && ischar(varargin{1})
    gui_State.gui_Callback = str2func(varargin{1});
end

if nargout
    [varargout{1:nargout}] = gui_mainfcn(gui_State, varargin{:});
else
    gui_mainfcn(gui_State, varargin{:});
end


function u11_guidex_OpeningFcn(hObject, eventdata, handles, varargin)
u11_log(sprintf('x opening fields=%s', strjoin(fieldnames(handles)', ',')));
handles.output = hObject;
guidata(hObject, handles);


function varargout = u11_guidex_OutputFcn(hObject, eventdata, handles)
varargout{1} = handles.output;


function h1 = u11_guidex_LayoutFcn(policy)
u11_log(['x layout ' policy]);
h1 = figure('Visible', 'off', 'Tag', 'figure1', 'Name', 'u11_guidex', 'IntegerHandle', 'off', ...
    'HandleVisibility', 'callback', 'MenuBar', 'none', 'NumberTitle', 'off');
uicontrol(h1, 'Style', 'text', 'Tag', 'text1', 'String', 'exported');
setappdata(h1, 'GUIDEOptions', struct('active_h', [], 'taginfo', [], 'override', 0, 'release', 13, ...
    'resize', 'none', 'accessibility', 'callback', 'mfile', 1, 'callbacks', 1, 'singleton', 1, ...
    'syscolorfig', 1, 'blocking', 0, 'lastSavedFile', ''));
