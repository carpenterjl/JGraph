function varargout = u11_guidev(varargin)
% U11_GUIDE1 MATLAB code for u11_guidev.fig, in the shape GUIDE generated (U11 fixtures). The four
% such apps share this text under their own names; tools/matlab-checklist/ui-probes/u11/u11_makefix.m
% writes the copies and every figure, the first by hgsave (the struct form), the rest by savefig.
%      U11_GUIDE1, by itself, creates a new U11_GUIDE1 or raises the existing singleton*.
%      H = U11_GUIDE1 returns the handle to a new U11_GUIDE1 or the handle to the existing singleton*.
%      U11_GUIDE1('CALLBACK',hObject,eventData,handles,...) calls the local function named CALLBACK.

% Begin initialization code - DO NOT EDIT
gui_Singleton = 1;
gui_State = struct('gui_Name',       mfilename, ...
                   'gui_Singleton',  gui_Singleton, ...
                   'gui_OpeningFcn', @u11_guidev_OpeningFcn, ...
                   'gui_OutputFcn',  @u11_guidev_OutputFcn, ...
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


% --- Executes just before u11_guidev is made visible.
function u11_guidev_OpeningFcn(hObject, eventdata, handles, varargin)
u11_log(sprintf('opening nargs=%d visible=%s hv=%s init=%d fields=%s', numel(varargin), ...
    get(hObject, 'Visible'), get(hObject, 'HandleVisibility'), isappdata(hObject, 'InGUIInitialization'), ...
    strjoin(fieldnames(handles)', ',')));
handles.output = hObject;
handles.count = 0;
handles.openingArgs = numel(varargin);
guidata(hObject, handles);


% --- Outputs from this function are returned to the command line.
function varargout = u11_guidev_OutputFcn(hObject, eventdata, handles)
u11_log(sprintf('output nargout=%d visible=%s', nargout, get(hObject, 'Visible')));
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
u11_log(sprintf('edit1 create handles=%s', class(handles)));
if ispc && isequal(get(hObject,'BackgroundColor'), get(0,'defaultUicontrolBackgroundColor'))
    set(hObject,'BackgroundColor','white');
end
