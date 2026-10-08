function varargout = myguidex(varargin)
% MYGUIDEX A GUIDE app exported to one file: gui_LayoutFcn builds the figure, no .fig.
gui_Singleton = 1;
gui_State = struct('gui_Name',       mfilename, ...
                   'gui_Singleton',  gui_Singleton, ...
                   'gui_OpeningFcn', @myguidex_OpeningFcn, ...
                   'gui_OutputFcn',  @myguidex_OutputFcn, ...
                   'gui_LayoutFcn',  @myguidex_LayoutFcn, ...
                   'gui_Callback',   []);
if nargin && ischar(varargin{1})
    gui_State.gui_Callback = str2func(varargin{1});
end

if nargout
    [varargout{1:nargout}] = gui_mainfcn(gui_State, varargin{:});
else
    gui_mainfcn(gui_State, varargin{:});
end


function myguidex_OpeningFcn(hObject, eventdata, handles, varargin)
u11log(sprintf('x opening: fields=%s', strjoin(fieldnames(handles)', ',')));
handles.output = hObject;
guidata(hObject, handles);


function varargout = myguidex_OutputFcn(hObject, eventdata, handles)
varargout{1} = handles.output;


function h1 = myguidex_LayoutFcn(policy)
u11log(['x layout ' policy]);
h1 = figure('Visible', 'off', 'Tag', 'figure1', 'Name', 'myguidex', 'IntegerHandle', 'off', ...
    'HandleVisibility', 'callback', 'MenuBar', 'none', 'NumberTitle', 'off');
uicontrol(h1, 'Style', 'text', 'Tag', 'text1', 'String', 'exported');
setappdata(h1, 'GUIDEOptions', struct('active_h', [], 'taginfo', [], 'override', 0, 'release', 13, ...
    'resize', 'none', 'accessibility', 'callback', 'mfile', 1, 'callbacks', 1, 'singleton', 1, ...
    'syscolorfig', 1, 'blocking', 0, 'lastSavedFile', ''));
