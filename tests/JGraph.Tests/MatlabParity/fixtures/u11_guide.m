% record: -noFigureWindows
% U11 of the app-building plan (ADR 0210): GUIDE apps under gui_mainfcn - research C's hand-built
% pair as written, and four GUIDE-shaped apps whose figures R2025b wrote in each form (hgsave for
% u11_guide1, savefig for the rest), one saved visible, one not a singleton, and one exported to a
% layout function - and guide itself. Probe u11_guide.
u11_log();
u9b_chk('guide', @() errorId(@() guide()));
u9b_chkdiv('guide_msg', @() errorMessage(@() guide()), '0210');
u9b_chk('guide_file', @() errorId(@() guide('x.fig')));

% --- research C's pair, as written
h = myguide('Visible', 'off');
hs = guidata(h);
u9b_chk('research_open', @() {h.Visible, h.Tag, h.HandleVisibility, fieldnames(hs)', hs.edit1.BackgroundColor, hs.openingArgs});
cb = get(hs.pushbutton1, 'Callback');
cb(hs.pushbutton1, []);
hs = guidata(h);
u9b_chk('research_callback', @() {hs.count, get(hs.text1, 'String')});
u9b_chk('research_singleton', @() isequal(myguide('Visible', 'off'), h));
delete(h);

% --- the contract, in each form
for name = {'u11_guide1', 'u11_guide2'}
    n = name{1};
    fn = str2func(n);
    u11_log();
    h = fn('Visible', 'off');
    u9b_chk([n '_log'], @() u11_log());
    u9b_chk([n '_figure'], @() {h.Visible, h.HandleVisibility, h.Tag, h.Name, isempty(h.Number), h.IntegerHandle, ...
        isappdata(h, 'InGUIInitialization'), endsWith(h.FileName, [n '.fig'])});
    hs = guidata(h);
    u9b_chk([n '_handles'], @() {fieldnames(hs)', hs.openingArgs, hs.edit1.BackgroundColor});
    cb = get(hs.pushbutton1, 'Callback');
    cb(hs.pushbutton1, []);
    hs = guidata(h);
    u9b_chk([n '_callback'], @() {hs.count, get(hs.text1, 'String')});
    fn('pushbutton1_Callback', hs.pushbutton1, [], guidata(h));
    u9b_chk([n '_by_name'], @() get(hs.text1, 'String'));
    u9b_chk([n '_no_function'], @() errorId(@() fn('pushbutton1_Nothing', hs.pushbutton1, [], guidata(h))));
    fn('edit1_CreateFcn', hs.edit1, [], guidata(h));
    u9b_chk([n '_createfcn_by_name'], @() u11_log());
    u11_log();
    h2 = fn('Visible', 'off', 'Name', 'Again');
    u9b_chk([n '_singleton'], @() {isequal(h2, h), h2.Name, u11_log()});
    clear ans
    fn('Visible', 'off');
    u9b_chk([n '_statement'], @() {u11_log(), exist('ans', 'var')});
    u9b_chk([n '_numeric_first'], @() {isequal(fn(7, 'x'), h), u11_log()});
    u9b_chk([n '_lone_word'], @() {isequal(fn('extra'), h), h.Visible, u11_log()});
    u9b_chk([n '_unknown_pair'], @() {isgraphics(fn('Visible', 'off', 'NoSuchProp', 1)), u11_log()});
    u9b_chk([n '_unmatched_name'], @() {isequal(fn('nosuch_Callback', hs.pushbutton1, [], []), h), u11_log()});
    delete(h);
    h = fn('Visible', 'off');
    u9b_chk([n '_after_delete'], @() u11_log());
    delete(h);
end

% --- shown as saved, unless asked
u11_log();
h = u11_guide1;
u9b_chk('saved_hidden', @() {h.Visible, isequal(get(groot, 'CurrentFigure'), h), u11_log()});
delete(h);
h = u11_guidev;
u9b_chk('saved_visible', @() {h.Visible, u11_log()});
delete(h);
h = u11_guidev('Visible', 'off');
u9b_chk('saved_visible_kept_hidden', @() h.Visible);
delete(h);
h = openfig(fullfile(pwd, 'helpers', 'u11_guidev.fig'));
u9b_chk('saved_visible_openfig', @() {h.Visible, h.HandleVisibility, u11_log()});
delete(h);

% --- not a singleton, and a layout function
a = u11_guide0('Visible', 'off');
b = u11_guide0('Visible', 'off');
u9b_chk('not_singleton', @() isequal(a, b));
delete([a b]);
u11_log();
h = u11_guidex('Visible', 'off');
u9b_chk('layout', @() {u11_log(), h.Tag, h.Visible});
h2 = u11_guidex('Visible', 'off');
u9b_chk('layout_again', @() {isequal(h, h2), u11_log()});
delete([h h2]);

% --- gui_mainfcn itself
u9b_chk('mainfcn_no_args', @() gui_mainfcn());
u9b_chk('mainfcn_no_figure', @() gui_mainfcn(struct('gui_Name', 'u11_nofig_here', 'gui_Singleton', 1, ...
    'gui_OpeningFcn', [], 'gui_OutputFcn', [], 'gui_LayoutFcn', [], 'gui_Callback', [])));

function text = errorMessage(fn)
% The message of the error fn raises.
try
    fn();
    text = 'no error';
catch err
    text = err.message;
end
end

function id = errorId(fn)
% The identifier of the error fn raises (its message names the source's class, ADR 0051).
try
    fn();
    id = 'no error';
catch err
    id = err.identifier;
end
end
