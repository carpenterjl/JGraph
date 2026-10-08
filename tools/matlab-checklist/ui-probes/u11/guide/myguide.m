function varargout = myguide(varargin)
% MYGUIDE MATLAB code for myguide.fig (hand-written in the shape GUIDE generated)
%      MYGUIDE, by itself, creates a new MYGUIDE or raises the existing
%      singleton*.
%
%      H = MYGUIDE returns the handle to a new MYGUIDE or the handle to
%      the existing singleton*.
%
%      MYGUIDE('CALLBACK',hObject,eventData,handles,...) calls the local
%      function named CALLBACK in MYGUIDE.M with the given input arguments.

% Begin initialization code - DO NOT EDIT
gui_Singleton = 1;
gui_State = struct('gui_Name',       mfilename, ...
                   'gui_Singleton',  gui_Singleton, ...
                   'gui_OpeningFcn', @myguide_OpeningFcn, ...
                   'gui_OutputFcn',  @myguide_OutputFcn, ...
                   'gui_LayoutFcn',  [] , ...
                   'gui_Callback',   []);
if nargin && ischar(varargin{1})
    gui_State.gui_Callback = str2func(varargin{1});
end

if nargout
    [varargout{1:nargout}] = gui_mainfcn(gui_State, varargin{:});
else
    gui_mainfcn(gui_State, varargin{:});
end
% End initialization code - DO NOT EDIT


% --- Executes just before myguide is made visible.
function myguide_OpeningFcn(hObject, eventdata, handles, varargin)
u11log(sprintf('opening: nargs=%d visible=%s hv=%s init=%d fields=%s', numel(varargin), ...
    get(hObject, 'Visible'), get(hObject, 'HandleVisibility'), isappdata(hObject, 'InGUIInitialization'), ...
    strjoin(fieldnames(handles)', ',')));
% Choose default command line output for myguide
handles.output = hObject;
handles.count = 0;
handles.openingArgs = numel(varargin);
% Update handles structure
guidata(hObject, handles);
% UIWAIT makes myguide wait for user response (see UIRESUME)
% uiwait(handles.figure1);


% --- Outputs from this function are returned to the command line.
function varargout = myguide_OutputFcn(hObject, eventdata, handles)
u11log(sprintf('output: nargout=%d visible=%s', nargout, get(hObject, 'Visible')));
varargout{1} = handles.output;


% --- Executes on button press in pushbutton1.
function pushbutton1_Callback(hObject, eventdata, handles)
handles.count = handles.count + str2double(get(handles.edit1, 'String'));
set(handles.text1, 'String', sprintf('Count: %d', handles.count));
guidata(hObject, handles);


function edit1_Callback(hObject, eventdata, handles)
% hObject    handle to edit1 (see GCBO)


% --- Executes during object creation, after setting all properties.
function edit1_CreateFcn(hObject, eventdata, handles)
u11log(sprintf('edit1 create: handles=%s', class(handles)));
if ispc && isequal(get(hObject,'BackgroundColor'), get(0,'defaultUicontrolBackgroundColor'))
    set(hObject,'BackgroundColor','white');
end
