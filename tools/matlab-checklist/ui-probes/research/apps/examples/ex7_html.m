function ex7_html
% EX7_HTML  A web page in an app (app-building stage U9b): ex7_html.html shows the component's Data
% and counts up from its own button, and MATLAB hears each count through DataChangedFcn; the
% "Greet the page" button sends the page an event, which it shows and answers with an event of its
% own; "Count from MATLAB" writes Data, which the page shows; "Alert" puts a uialert over the page.
% Every component is tagged, so a window check can find it by its tag.

here = fileparts(mfilename('fullpath'));
fig = uifigure('Name', 'HTML component', 'Position', [100 100 460 360], 'Tag', 'ex7');
g = uigridlayout(fig, [3 3]);
g.RowHeight = {'1x', 'fit', 'fit'};

page = uihtml(g, 'HTMLSource', fullfile(here, 'ex7_html.html'), 'Data', 0, 'Tag', 'page');
page.Layout.Row = 1;
page.Layout.Column = [1 3];

count = uilabel(g, 'Text', 'Page count: 0', 'Tag', 'count');
count.Layout.Row = 2;
count.Layout.Column = [1 2];
heard = uilabel(g, 'Text', 'Nothing heard yet', 'Tag', 'heard');
heard.Layout.Row = 2;
heard.Layout.Column = 3;

uibutton(g, 'Text', 'Greet the page', 'Tag', 'greet', ...
    'ButtonPushedFcn', @(~, ~) sendEventToHTMLSource(page, 'greet', 'Hello from MATLAB'));
uibutton(g, 'Text', 'Count from MATLAB', 'Tag', 'more', ...
    'ButtonPushedFcn', @(~, ~) set(page, 'Data', page.Data + 10));
uibutton(g, 'Text', 'Alert', 'Tag', 'alert', ...
    'ButtonPushedFcn', @(~, ~) uialert(fig, 'An alert over the page.', 'Alert'));

page.DataChangedFcn = @(src, event) set(count, 'Text', sprintf('Page count: %g (was %g)', src.Data, event.PreviousData));
page.HTMLEventReceivedFcn = @(~, event) set(heard, 'Text', sprintf('%s: %g', event.HTMLEventName, event.HTMLEventData));
fprintf('ex7_html ready\n');
end
